using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BlenderTool
{
    public class RenderQueue
    {
        public List<RenderJob> Jobs { get; } = new();

        // Progress: (completedJobs, totalJobs, currentJobOutput)
        public event Action<int, int, string>? ProgressChanged;
        public event Action? QueueFinished;
        /// <summary>Fired whenever a job's SecondsPerFrame estimate is updated.</summary>
        public event Action? EstimateUpdated;

        private CancellationTokenSource? _cts;
        private Process? _currentProcess;

        public bool IsRunning => _cts != null;

        // ── Blender output line: "Fra:42 Mem:..." ─────────────
        private static readonly Regex _frameRegex =
            new Regex(@"^Fra:(\d+)\s", RegexOptions.Compiled);

        // ── Blender frame-time line ────────────────────────────
        // Blender 5 prints: "00:05.922  render  | Time: 00:02.84 (Saving: 00:00.17)"
        // Match "Time: MM:SS.ff" anywhere after the pipe; groups: (1)=MM (2)=SS (3)=ff
        private static readonly Regex _timeRegex =
            new Regex(@"\|\s*Time:\s*(\d+):(\d{2})\.(\d+)",
                      RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public async Task StartAsync()
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();

            int total = Jobs.Count;
            int done  = 0;

            foreach (var job in Jobs)
            {
                if (_cts.Token.IsCancellationRequested) break;
                if (job.Status == JobStatus.Done) { done++; continue; }

                // For full-timeline jobs, detect frame range now if not already known
                // so that EstimatedTimeDisplay works during and after the render.
                bool isFullTimeline = string.IsNullOrWhiteSpace(job.FrameStart);
                if (isFullTimeline && !job.DetectedFrameStart.HasValue)
                {
                    ProgressChanged?.Invoke(done, total,
                        $"Detecting frame range: {Path.GetFileName(job.BlendFile)}…");
                    await DetectFrameRangeAsync(job, _cts.Token);

                    // Propagate to other full-timeline jobs sharing the same blend file
                    if (job.DetectedFrameStart.HasValue)
                    {
                        foreach (var other in Jobs)
                        {
                            if (other == job) continue;
                            if (string.Equals(other.BlendFile, job.BlendFile,
                                    StringComparison.OrdinalIgnoreCase)
                                && string.IsNullOrWhiteSpace(other.FrameStart))
                            {
                                other.DetectedFrameStart = job.DetectedFrameStart;
                                other.DetectedFrameEnd   = job.DetectedFrameEnd;
                            }
                        }
                    }

                    EstimateUpdated?.Invoke();
                }

                job.Status = JobStatus.Rendering;
                ProgressChanged?.Invoke(done, total, $"Rendering: {Path.GetFileName(job.BlendFile)}");

                bool success = await RunJobAsync(job, _cts.Token, trackTiming: true);

                job.Status = success ? JobStatus.Done : JobStatus.Failed;
                done++;
                ProgressChanged?.Invoke(done, total,
                    success ? $"Done: {Path.GetFileName(job.BlendFile)}"
                            : $"FAILED: {Path.GetFileName(job.BlendFile)}");
            }

            _cts = null;
            QueueFinished?.Invoke();
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _currentProcess?.Kill(entireProcessTree: true); } catch { }
        }

        // ── Detect scene frame range from a .blend file ───────
        /// <summary>
        /// Runs Blender in background-Python mode to read the scene's
        /// frame_start / frame_end from the render settings and stores
        /// the result in <paramref name="job"/>.DetectedFrameStart/End.
        /// No frames are rendered; the process exits immediately after
        /// the Python expression is evaluated.
        /// </summary>
        public async Task DetectFrameRangeAsync(RenderJob job, CancellationToken ct)
        {
            var blender = AppSettings.GetBlenderPath();
            if (!File.Exists(blender)) return;

            // Blender prints our marker line to stdout so we can parse it reliably
            // even if other info lines are mixed in.
            const string marker = "RKNZ_FRAME_RANGE:";
            var pyExpr = $"import bpy; s=bpy.context.scene; print('{marker}'+str(s.frame_start)+':'+str(s.frame_end))";
            var args   = $"-b \"{job.BlendFile}\" --python-expr \"{pyExpr}\"";

            var psi = new ProcessStartInfo(blender, args)
            {
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            int? detectedStart = null;
            int? detectedEnd   = null;

            var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                int idx = e.Data.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0) return;
                var payload = e.Data.Substring(idx + marker.Length);
                var parts   = payload.Split(':');
                if (parts.Length == 2
                    && int.TryParse(parts[0], out int fs)
                    && int.TryParse(parts[1], out int fe))
                {
                    detectedStart = fs;
                    detectedEnd   = fe;
                }
            };
            // stderr is not needed but must be redirected to avoid blocking
            proc.ErrorDataReceived += (s, e) => { };

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                await proc.WaitForExitAsync(ct);

                if (detectedStart.HasValue && detectedEnd.HasValue)
                {
                    job.DetectedFrameStart = detectedStart;
                    job.DetectedFrameEnd   = detectedEnd;
                }
            }
            catch (OperationCanceledException) { }
            catch { /* silently skip detection failures */ }
            finally
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            }
        }


        /// <summary>
        /// Renders exactly one frame from each unique blend file in the queue so that
        /// SecondsPerFrame can be estimated before the full render starts.
        /// </summary>
        public async Task SampleAllAsync(CancellationToken ct)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var job in Jobs)
            {
                if (ct.IsCancellationRequested) break;
                if (seen.Contains(job.BlendFile)) continue;
                seen.Add(job.BlendFile);

                // For full-timeline jobs, detect the scene's frame range from
                // the .blend file first so FrameCount (and thus Est. Time) works.
                bool isFullTimeline = string.IsNullOrWhiteSpace(job.FrameStart);
                if (isFullTimeline && !job.DetectedFrameStart.HasValue)
                {
                    ProgressChanged?.Invoke(-1, -1,
                        $"Detecting frame range: {Path.GetFileName(job.BlendFile)}…");
                    await DetectFrameRangeAsync(job, ct);

                    // Copy detected range to every other full-timeline job sharing the same file
                    if (job.DetectedFrameStart.HasValue)
                    {
                        foreach (var other in Jobs)
                        {
                            if (other == job) continue;
                            if (string.Equals(other.BlendFile, job.BlendFile,
                                    StringComparison.OrdinalIgnoreCase)
                                && string.IsNullOrWhiteSpace(other.FrameStart))
                            {
                                other.DetectedFrameStart = job.DetectedFrameStart;
                                other.DetectedFrameEnd   = job.DetectedFrameEnd;
                            }
                        }
                    }

                    EstimateUpdated?.Invoke();
                }

                ProgressChanged?.Invoke(-1, -1, $"Sampling: {Path.GetFileName(job.BlendFile)}…");
                await RunJobAsync(job, ct, trackTiming: true, sampleOnly: true);
                EstimateUpdated?.Invoke();
            }
            ProgressChanged?.Invoke(-1, -1, "Sampling complete.");
        }

        // ─────────────────────────────────────────────────────
        private async Task<bool> RunJobAsync(
            RenderJob job,
            CancellationToken ct,
            bool trackTiming  = false,
            bool sampleOnly   = false)
        {
            var blender = AppSettings.GetBlenderPath();
            if (!File.Exists(blender)) return false;

            // Decide frame args
            string frameArgs;
            if (sampleOnly)
            {
                // Render only 1 frame for sampling
                int sampleFrame = int.TryParse(job.FrameStart, out int fs) ? fs : 1;
                frameArgs = $" -s {sampleFrame} -e {sampleFrame}";
            }
            else if (!string.IsNullOrWhiteSpace(job.FrameStart))
            {
                frameArgs = $" -s {job.FrameStart} -e {job.FrameEnd}";
            }
            else
            {
                frameArgs = string.Empty;
            }

            var args = $"-b \"{job.BlendFile}\"";
            if (!string.IsNullOrWhiteSpace(job.OutputPath))
                args += $" -o \"{job.OutputPath.TrimEnd('\\', '/')}\\\\\"";
            args += frameArgs;
            args += " -a";

            var psi = new ProcessStartInfo(blender, args)
            {
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            _currentProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            void HandleLine(string line)
            {
                if (string.IsNullOrEmpty(line)) return;
                ProgressChanged?.Invoke(-1, -1, line);

                if (!trackTiming) return;

                // Blender 5 prints "| Time: MM:SS.ff (Saving: ...)" once per completed frame.
                // Groups: (1)=minutes (2)=seconds (3)=centiseconds
                var m = _timeRegex.Match(line);
                if (m.Success)
                {
                    double minutes = int.Parse(m.Groups[1].Value);
                    double seconds = int.Parse(m.Groups[2].Value);
                    string fracStr = m.Groups[3].Value;
                    double frac    = double.Parse(fracStr) / Math.Pow(10, fracStr.Length);
                    double frameSeconds = minutes * 60 + seconds + frac;
                    if (frameSeconds > 0)
                    {
                        UpdateJobEstimate(job, frameSeconds);
                        EstimateUpdated?.Invoke();
                    }
                }
            }

            _currentProcess.OutputDataReceived += (s, e) => { if (e.Data != null) HandleLine(e.Data); };
            _currentProcess.ErrorDataReceived  += (s, e) => { if (e.Data != null) HandleLine(e.Data); };

            try
            {
                _currentProcess.Start();
                _currentProcess.BeginOutputReadLine();
                _currentProcess.BeginErrorReadLine();

                await _currentProcess.WaitForExitAsync(ct);

                return _currentProcess.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            finally
            {
                _currentProcess = null;
            }
        }

        /// <summary>Rolling average update for SecondsPerFrame.</summary>
        private static void UpdateJobEstimate(RenderJob job, double secondsThisFrame)
        {
            if (secondsThisFrame <= 0) return;
            if (job.SecondsPerFrame == null)
            {
                job.SecondsPerFrame  = secondsThisFrame;
                job.TimedFrameCount  = 1;
            }
            else
            {
                job.TimedFrameCount++;
                // Exponential moving average (α = 1/n) keeps it responsive early
                // and stable later
                job.SecondsPerFrame = job.SecondsPerFrame +
                    (secondsThisFrame - job.SecondsPerFrame) / job.TimedFrameCount;
            }

            // Propagate the same SPF to other jobs that share the same blend file
            // (they will be rendered with the same scene complexity)
        }

        /// <summary>
        /// After updating one job's SPF, copy it to every other queued job that
        /// shares the same blend file.  Call from UI thread or lock if needed.
        /// </summary>
        public void PropagateEstimates()
        {
            foreach (var job in Jobs)
            {
                if (job.SecondsPerFrame == null) continue;
                foreach (var other in Jobs)
                {
                    if (other == job) continue;
                    if (string.Equals(other.BlendFile, job.BlendFile,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        // Only update if we have a better (more-sampled) estimate
                        if (other.TimedFrameCount < job.TimedFrameCount)
                        {
                            other.SecondsPerFrame  = job.SecondsPerFrame;
                            other.TimedFrameCount  = job.TimedFrameCount;
                        }
                    }
                }
            }
        }
    }
}

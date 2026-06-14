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
        // ONLY on the completed-frame summary line (which always includes "Saving:").
        // Intermediate per-sample progress lines also contain "Time:" with partial
        // elapsed values — matching those inflates the rolling average and produces
        // estimates far too low.  Requiring "Saving:" in the same line restricts
        // the match to the final per-frame summary only.
        // Groups: (1)=minutes (2)=seconds (3)=fractional-seconds digits
        private static readonly Regex _timeRegex =
            new Regex(@"\|\s*Time:\s*(\d+):(\d{2})\.(\d+)[^|]*\(Saving:",
                      RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ── Viewport (OpenGL) render: saved-image report line ──
        // Blender prints "MM:SS.fff  render  | Saved: 'C:\path\0001.exr'" for every frame.
        // We parse the leading wall-clock timestamp and subtract consecutive frames
        // to get per-frame render time (frame 2 timestamp − frame 1 timestamp),
        // which strips the one-time startup cost from the estimate.
        // Groups: (1)=MM  (2)=SS  (3)=fractional-seconds digits
        private static readonly Regex _viewportSavedRegex =
            new Regex(@"^(\d+):(\d{2})\.(\d+).*\|\s*Saved:",
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

        // ── Viewport (OpenGL) render support ──────────────────
        /// <summary>
        /// Script run via --python for viewport-render jobs. It locates the
        /// 3D viewport, applies the requested shading mode, switches the view
        /// to look through the scene's active camera (so the render always
        /// matches the camera framing rather than the last-saved viewport
        /// angle), performs the OpenGL render, and quits Blender.
        /// "__SHADING_TYPE__" is replaced with one of WIREFRAME / SOLID /
        /// MATERIAL / RENDERED before the script is written to disk.
        /// </summary>
        private const string ViewportRenderScriptTemplate = @"
import bpy
import os

try:
    scene = bpy.context.scene
    win = bpy.context.window_manager.windows[0]
    screen = win.screen

    view3d_area = None
    view3d_region = None
    view3d_space = None
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            view3d_area = area
            view3d_space = area.spaces[0]
            for region in area.regions:
                if region.type == 'WINDOW':
                    view3d_region = region
            break

    if view3d_area is None or view3d_region is None:
        print('RKNZ_VIEWPORT_ERROR: No 3D Viewport area found')
        os._exit(1)

    if scene.camera is None:
        print('RKNZ_VIEWPORT_ERROR: Scene has no active camera')
        os._exit(1)

    # Apply the requested viewport shading mode
    view3d_space.shading.type = '__SHADING_TYPE__'

    # Hide overlays (grid, bone names, object outlines, etc.) so they don't
    # appear in the rendered frames.
    view3d_space.overlay.show_overlays = False

    # Look through the active camera so the render matches the camera framing
    # instead of wherever the viewport was last left.
    view3d_region.data.view_perspective = 'CAMERA'

    with bpy.context.temp_override(window=win, area=view3d_area, region=view3d_region):
        bpy.ops.render.opengl(animation=True)

except Exception as ex:
    print('RKNZ_VIEWPORT_ERROR: ' + str(ex))
    os._exit(1)

bpy.ops.wm.quit_blender()
";

        /// <summary>Writes a per-job viewport render script to a temp file and returns its path.</summary>
        /// <param name="sampleOutputDir">
        /// When set (sample runs only), the script patches the scene's render filepath and
        /// enables overwrite before calling render.opengl, so the project's own output path
        /// and overwrite flag are left untouched.
        /// </param>
        private static string WriteViewportRenderScript(RenderJob job, string? sampleOutputDir = null)
        {
            var script = ViewportRenderScriptTemplate.Replace("__SHADING_TYPE__", job.ShadingTypeBlenderValue);

            if (sampleOutputDir != null)
            {
                // Escape backslashes for the Python string literal.
                var escaped = sampleOutputDir.Replace("\\", "\\\\");
                var patch =
                    $"\n    scene.render.filepath = '{escaped}\\\\'" +
                     "\n    scene.render.use_overwrite = True\n";

                // Inject right before the opengl call so the path is set in time.
                script = script.Replace(
                    "    with bpy.context.temp_override",
                    patch + "    with bpy.context.temp_override");
            }

            var path = Path.Combine(Path.GetTempPath(), $"rknz_viewport_{Guid.NewGuid():N}.py");
            File.WriteAllText(path, script);
            return path;
        }

        /// <summary>
        /// Creates a disposable temp directory for sample renders and returns its path.
        /// The caller is responsible for deleting it when the sample run finishes.
        /// </summary>
        private static string CreateSampleOutputDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"rknz_sample_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
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
                int sampleFrame = int.TryParse(job.FrameStart, out int fs) ? fs : 1;

                if (job.UseViewportRender)
                {
                    // Viewport render doesn't print per-frame render time; instead we
                    // read the wall-clock timestamp on each "Saved:" line and subtract
                    // consecutive timestamps to get frame cost.  We need at least 2
                    // frames so the subtraction is possible (frame 1 captures startup
                    // overhead; frame 2 − frame 1 gives the pure render time).
                    frameArgs = $" -s {sampleFrame} -e {sampleFrame + 1}";
                }
                else
                {
                    // Final render prints "| Time: ..." per frame, so 1 frame suffices.
                    frameArgs = $" -s {sampleFrame} -e {sampleFrame}";
                }
            }
            else if (!string.IsNullOrWhiteSpace(job.FrameStart))
            {
                frameArgs = $" -s {job.FrameStart} -e {job.FrameEnd}";
            }
            else
            {
                frameArgs = string.Empty;
            }

            // For sample runs we redirect output to a private temp folder and force
            // overwrite so the project's own output path and overwrite flag are never
            // touched.  The folder is deleted once the sample process exits.
            string? sampleOutputDir = sampleOnly ? CreateSampleOutputDir() : null;

            var args = job.UseViewportRender
                ? $"\"{job.BlendFile}\""
                : $"-b \"{job.BlendFile}\"";

            if (sampleOnly)
            {
                // Override output to temp dir and force overwrite via a python-expr so
                // the project's use_overwrite setting cannot block the sample render.
                args += $" -o \"{sampleOutputDir!.TrimEnd('\\', '/')}\\\\\"";
                if (!job.UseViewportRender)
                    args += " --python-expr \"import bpy; bpy.context.scene.render.use_overwrite = True\"";
                // Viewport overwrite is handled inside WriteViewportRenderScript.
            }
            else if (!string.IsNullOrWhiteSpace(job.OutputPath))
            {
                args += $" -o \"{job.OutputPath.TrimEnd('\\', '/')}\\\\\"";
            }
            args += frameArgs;

            // Viewport (OpenGL) render needs a real 3D viewport / GL context, which
            // background mode (-b) does not provide. So for viewport jobs we launch
            // Blender normally (it briefly shows its window) and hand it a small
            // script that switches the viewport to the active camera, applies the
            // requested shading mode, runs the OpenGL render, then quits.
            string? viewportScriptPath = null;
            if (job.UseViewportRender)
            {
                viewportScriptPath = WriteViewportRenderScript(job, sampleOutputDir);
                args += $" --python \"{viewportScriptPath}\"";
            }
            else
            {
                args += " -a";
            }

            var psi = new ProcessStartInfo(blender, args)
            {
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            _currentProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // State for viewport-render timing: we track the wall-clock timestamp
            // printed on each "Saved:" line and diff consecutive frames.
            // Frame 1 timestamp captures startup; frame 2 − frame 1 = pure render time.
            double? viewportPrevTimestamp = null;

            void HandleLine(string line)
            {
                if (string.IsNullOrEmpty(line)) return;
                ProgressChanged?.Invoke(-1, -1, line);

                if (!trackTiming) return;

                if (job.UseViewportRender)
                {
                    // Viewport render prints "MM:SS.fff  Saved: '/path/frame####.png'"
                    // for every saved frame.  Parse the leading wall-clock timestamp and
                    // subtract the previous frame's timestamp to get per-frame cost.
                    var vm = _viewportSavedRegex.Match(line);
                    if (vm.Success)
                    {
                        double mins    = int.Parse(vm.Groups[1].Value);
                        double secs    = int.Parse(vm.Groups[2].Value);
                        string fracStr = vm.Groups[3].Value;
                        double frac    = double.Parse(fracStr) / Math.Pow(10, fracStr.Length);
                        double ts      = mins * 60 + secs + frac;

                        if (viewportPrevTimestamp.HasValue)
                        {
                            double frameSeconds = ts - viewportPrevTimestamp.Value;
                            if (frameSeconds > 0)
                            {
                                UpdateJobEstimate(job, frameSeconds);
                                EstimateUpdated?.Invoke();
                            }
                        }
                        // Always store; even frame 1 is needed as the baseline for frame 2.
                        viewportPrevTimestamp = ts;
                    }
                }
                else
                {
                    // Final render: Blender 5 prints "| Time: MM:SS.ff (Saving: ...)" once
                    // per completed frame; the value is the cumulative render time for that
                    // frame alone (not a running total), so use it directly.
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
                if (viewportScriptPath != null)
                    try { File.Delete(viewportScriptPath); } catch { }
                if (sampleOutputDir != null)
                    try { Directory.Delete(sampleOutputDir, recursive: true); } catch { }
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

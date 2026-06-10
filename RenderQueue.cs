using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        private CancellationTokenSource? _cts;
        private Process? _currentProcess;

        public bool IsRunning => _cts != null;

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

                job.Status = JobStatus.Rendering;
                ProgressChanged?.Invoke(done, total, $"Rendering: {Path.GetFileName(job.BlendFile)}");

                bool success = await RunJobAsync(job, _cts.Token);

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

        private async Task<bool> RunJobAsync(RenderJob job, CancellationToken ct)
        {
            var blender = AppSettings.GetBlenderPath();
            if (!File.Exists(blender)) return false;

            // Build args:  blender -b file.blend [-o /path/] [-s 1 -e 50] -a
            var args = $"-b \"{job.BlendFile}\"";
            if (!string.IsNullOrWhiteSpace(job.OutputPath))
                args += $" -o \"{job.OutputPath.TrimEnd('\\', '/')}\\\\\"";
            if (!string.IsNullOrWhiteSpace(job.FrameStart))
                args += $" -s {job.FrameStart} -e {job.FrameEnd}";
            args += " -a";

            var psi = new ProcessStartInfo(blender, args)
            {
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            _currentProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            // Relay output lines to progress event
            _currentProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    ProgressChanged?.Invoke(-1, -1, e.Data);
            };

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
    }
}

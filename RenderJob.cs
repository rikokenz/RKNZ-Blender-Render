using System;

namespace BlenderTool
{
    public enum JobStatus { Waiting, Rendering, Done, Failed }

    /// <summary>Viewport shading modes available for viewport (OpenGL) renders.</summary>
    public enum ViewportShadingMode { Wireframe, Solid, Material, Rendered }

    public class RenderJob
    {
        public string BlendFile  { get; set; } = string.Empty;
        public string FrameStart { get; set; } = string.Empty;  // empty = use timeline
        public string FrameEnd   { get; set; } = string.Empty;
        public JobStatus Status  { get; set; } = JobStatus.Waiting;
        public string StatusText => Status switch
        {
            JobStatus.Waiting   => "Waiting",
            JobStatus.Rendering => "Rendering...",
            JobStatus.Done      => "Done",
            JobStatus.Failed    => "Failed",
            _                   => ""
        };

        public string OutputPath    { get; set; } = string.Empty;  // empty = use .blend default

        // ── Estimated time ────────────────────────────────────
        /// <summary>Rolling average seconds per frame, updated after each rendered frame.</summary>
        public double? SecondsPerFrame { get; set; } = null;

        /// <summary>How many frames have been timed (used for rolling average).</summary>
        public int TimedFrameCount { get; set; } = 0;

        public string FrameRangeDisplay =>
            string.IsNullOrWhiteSpace(FrameStart) ? "Full timeline"
            : $"{FrameStart} – {FrameEnd}";

        public string OutputDisplay =>
            string.IsNullOrWhiteSpace(OutputPath) ? "(default)" : OutputPath;

        // ── Viewport (OpenGL) render mode ─────────────────────
        /// <summary>
        /// When true, this job is rendered using Blender's viewport (OpenGL)
        /// render instead of the actual scene render. Before rendering, the
        /// 3D viewport is switched to look through the scene's active camera
        /// (so it isn't rendered from whatever angle it was last left at) and
        /// set to the shading mode in <see cref="ShadingMode"/>.
        /// </summary>
        public bool UseViewportRender { get; set; } = false;

        /// <summary>Viewport shading mode used when <see cref="UseViewportRender"/> is true.</summary>
        public ViewportShadingMode ShadingMode { get; set; } = ViewportShadingMode.Solid;

        /// <summary>Human-readable name of <see cref="ShadingMode"/>.</summary>
        public string ShadingModeDisplay => ShadingMode switch
        {
            ViewportShadingMode.Wireframe => "Wireframe",
            ViewportShadingMode.Solid      => "Solid",
            ViewportShadingMode.Material   => "Material Preview",
            ViewportShadingMode.Rendered   => "Rendered",
            _                               => "Solid"
        };

        /// <summary>Value expected by Blender's space_data.shading.type for <see cref="ShadingMode"/>.</summary>
        public string ShadingTypeBlenderValue => ShadingMode switch
        {
            ViewportShadingMode.Wireframe => "WIREFRAME",
            ViewportShadingMode.Solid      => "SOLID",
            ViewportShadingMode.Material   => "MATERIAL",
            ViewportShadingMode.Rendered   => "RENDERED",
            _                               => "SOLID"
        };

        /// <summary>Display string for the queue's Mode column.</summary>
        public string RenderModeDisplay =>
            UseViewportRender ? $"Viewport ({ShadingModeDisplay})" : "Final Render";

        // ── Frame range detected from the .blend file ─────────
        /// <summary>
        /// Scene start frame read from the .blend file's render settings.
        /// Populated by RenderQueue.DetectFrameRangeAsync; used as fallback
        /// when the job has no explicit FrameStart/FrameEnd override.
        /// </summary>
        public int? DetectedFrameStart { get; set; } = null;

        /// <summary>Scene end frame read from the .blend file's render settings.</summary>
        public int? DetectedFrameEnd   { get; set; } = null;

        /// <summary>Total frame count for this job.
        /// Uses the explicit override when set, otherwise falls back to the
        /// values detected from the .blend file.  Returns null only when
        /// neither source is available yet.</summary>
        public int? FrameCount
        {
            get
            {
                // Explicit override takes priority
                if (int.TryParse(FrameStart, out int s) && int.TryParse(FrameEnd, out int e2))
                    return Math.Max(0, e2 - s + 1);

                // Fall back to values detected from the .blend file
                if (DetectedFrameStart.HasValue && DetectedFrameEnd.HasValue)
                    return Math.Max(0, DetectedFrameEnd.Value - DetectedFrameStart.Value + 1);

                return null;
            }
        }

        /// <summary>Display string for the Estimated Time column.</summary>
        public string EstimatedTimeDisplay
        {
            get
            {
                if (Status == JobStatus.Done) return "—";
                if (SecondsPerFrame == null)  return "Process queue first";

                int? fc = FrameCount;
                if (fc == null)
                {
                    // Still waiting for blend-file detection
                    return "Detecting timeline…";
                }

                double secs = SecondsPerFrame.Value * fc.Value;
                return FormatSeconds(secs);
            }
        }

        public static string FormatSeconds(double totalSeconds)
        {
            var ts = TimeSpan.FromSeconds(totalSeconds);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
            if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes}m {ts.Seconds:D2}s";
            return $"{ts.Seconds}s";
        }
    }
}

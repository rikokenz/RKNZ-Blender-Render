using System;

namespace BlenderTool
{
    public enum JobStatus { Waiting, Rendering, Done, Failed }

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

        public string FrameRangeDisplay =>
            string.IsNullOrWhiteSpace(FrameStart) ? "Full timeline"
            : $"{FrameStart} – {FrameEnd}";

        public string OutputDisplay =>
            string.IsNullOrWhiteSpace(OutputPath) ? "(default)" : OutputPath;
    }
}

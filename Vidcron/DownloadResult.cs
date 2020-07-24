using System;

namespace Vidcron
{
    public class DownloadResult
    {
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DownloadStatus Status { get; set; }
        public Exception Error { get; set; }
    }
}
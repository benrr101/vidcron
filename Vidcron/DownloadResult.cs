using System;

namespace Vidcron
{
    public class DownloadResult
    {
        public DateTime? EndTime { get; set; }
        public Exception Error { get; set; }
        public DateTime StartTime { get; set; }
        public DownloadStatus Status { get; set; }
    }
}
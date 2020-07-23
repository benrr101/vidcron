using System;

namespace Vidcron
{
    public class DownloadResult
    {
        public string UniqueId { get; set; }
        public string DisplayName { get; set; }
        public DownloadStatus Status { get; set; }
        public Exception Error { get; set; }
    }
}
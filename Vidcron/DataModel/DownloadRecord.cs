using System;

namespace Vidcron.DataModel
{
    public class DownloadRecord
    {
        public string Id { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

    }
}
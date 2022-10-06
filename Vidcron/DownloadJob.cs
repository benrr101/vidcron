using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Vidcron
{
    public class DownloadJob
    {
        public Func<Task<DownloadResult>> RunJob { get; set; }

        public Func<Task<DownloadResult>> VerifyJob { get; set; }

        public string DisplayName { get; set; }

        public Logger Logger { get; set; }
        
        public DownloadResult Result { get; set; }
        
        public string SourceName { get; set; }
        
        public string UniqueId { get; set; }

        public class DownloadComparer : IEqualityComparer<DownloadJob>
        {
            public bool Equals(DownloadJob x, DownloadJob y)
            {
                if (x == null && y == null)
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return x.UniqueId == y.UniqueId;
            }

            public int GetHashCode(DownloadJob obj)
            {
                return obj.UniqueId.GetHashCode();
            }
        }
    }
}
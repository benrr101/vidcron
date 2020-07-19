using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Vidcron
{
    public class Download
    {
        public Func<Task<string>> ActionToPerform { get; set; }

        public string UniqueId { get; set; }

        public class DownloadComparer : IEqualityComparer<Download>
        {
            public bool Equals(Download x, Download y)
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

            public int GetHashCode(Download obj)
            {
                return obj.UniqueId.GetHashCode();
            }
        }
    }
}
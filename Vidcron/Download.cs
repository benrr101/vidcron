using System;
using System.Threading.Tasks;

namespace Vidchron
{
    public class Download
    {
        public Func<Task> ActionToPerform { get; set; }

        public string UniqueId { get; set; }
    }
}
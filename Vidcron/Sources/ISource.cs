using System.Collections.Generic;

namespace Vidcron.Sources
{
    public interface ISource
    {
        IEnumerable<Download> AllDownloads { get; }
    }
}
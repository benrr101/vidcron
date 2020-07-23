using System.Collections.Generic;
using System.Threading.Tasks;

namespace Vidcron.Sources
{
    public interface ISource
    {
        Task<IEnumerable<DownloadJob>> GetAllDownloads();
    }
}
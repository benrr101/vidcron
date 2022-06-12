using Microsoft.Extensions.Logging;

namespace Vidcron
{
    public class GlobalConfig
    {
        public LogLevel LogLevel { get; set; } = LogLevel.Information;

        public SourceConfig[] Sources { get; set; }
    }
}
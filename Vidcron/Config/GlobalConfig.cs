using System;
using Microsoft.Extensions.Logging;

namespace Vidcron.Config
{
    public class GlobalConfig
    {
        public EmailConfig Email { get; set; }
        
        public LogLevel LogLevel { get; set; } = LogLevel.Information;

        public int MaxConcurrentJobs { get; set; } = Environment.ProcessorCount;
        
        public SourceConfig[] Sources { get; set; }
    }
}
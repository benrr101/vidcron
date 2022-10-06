using System.Collections.Generic;

namespace Vidcron.Config
{
    public class SourceConfig
    {
        public SourceConfig()
        {
            Properties = new Dictionary<string, string>();
        }

        public string DestinationFolder { get; set; }

        public string Name { get; set; }

        public Dictionary<string, string> Properties { get; set; }

        public string Type { get; set; }
    }
}
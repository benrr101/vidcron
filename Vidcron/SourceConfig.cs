using System.Collections.Generic;

namespace Vidchron
{
    public class SourceConfig
    {
        public SourceConfig()
        {
            Properties = new Dictionary<string, string>();
        }

        public string Type { get; set; }

        public string Name { get; set; }

        public Dictionary<string, string> Properties { get; }
    }
}
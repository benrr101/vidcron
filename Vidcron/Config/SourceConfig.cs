using System;
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

        public string GetStringProperty(string key) =>
            Properties.TryGetValue(key, out var value)
                ? value
                : null;

        public bool? GetBooleanProperty(string key)
        {
            if (Properties.TryGetValue(key, out var value))
            {
                if (value.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (value.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            
            return null;
        }
    }
}
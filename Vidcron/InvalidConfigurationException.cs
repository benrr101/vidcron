using System;

namespace Vidchron
{
    public class InvalidConfigurationException : Exception
    {
        public InvalidConfigurationException(string message) : base(message) {}
    }
}
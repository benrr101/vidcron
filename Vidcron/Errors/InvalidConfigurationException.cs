using System;

namespace Vidcron.Errors
{
    public class InvalidConfigurationException : Exception
    {
        public InvalidConfigurationException(string message) : base(message) {}
    }
}
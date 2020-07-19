using System;

namespace Vidcron
{
    public class ProcessFailureException : Exception
    {
        public ProcessFailureException(string message) : base(message)
        {}
        
        public ProcessFailureException(string message, string stdOutput, string stdError) : base(message)
        {}
    }
}
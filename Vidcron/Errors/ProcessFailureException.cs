using System;
using System.Collections.Generic;

namespace Vidcron
{
    public class ProcessFailureException : Exception
    {
        public ProcessFailureException(
            string message,
            int exitCode,
            ICollection<string> stdOutput,
            ICollection<string> stdError
        ) : base(message)
        {
            ExitCode = exitCode;
            StandardError = stdError;
            StandardOutput = stdOutput;
        }

        public int ExitCode { get; }

        public ICollection<string> StandardError { get; }

        public ICollection<string> StandardOutput { get; }

    }
}
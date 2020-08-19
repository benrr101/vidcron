using System;

namespace Vidcron
{
    public class Logger
    {
        private readonly string _prefix;

        public Logger(string prefix)
        {
            _prefix = prefix;
        }

        public void Info(string message)
        {
            Console.WriteLine($"[{DateTime.Now:s}][{_prefix}][INFO] {message}");
        }

        public void Error(string message)
        {
            Console.Error.WriteLine($"[{DateTime.Now:s}][{_prefix}][ERROR] {message}");
        }

        public void Warn(string message)
        {
            Console.WriteLine($"[{DateTime.Now:s}][{_prefix}][WARN] {message}");
        }
    }
}
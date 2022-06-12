using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Vidcron
{
    public class Logger
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private readonly LogLevel _logLevel;

        private readonly string _prefix;

        public Logger(string prefix, LogLevel level)
        {
            _prefix = prefix;
            _logLevel = level;
        }

        public Task Debug(string message) =>
            _logLevel <= LogLevel.Debug ? Log(Console.Out, "DEBUG", message) : Task.CompletedTask;

        public Task Error(string message) =>
            _logLevel <= LogLevel.Error ? Log(Console.Error, "ERROR", message) : Task.CompletedTask;

        public Task Info(string message) =>
            _logLevel <= LogLevel.Information ? Log(Console.Out, "INFO", message) : Task.CompletedTask;

        public Task Warn(string message) =>
            _logLevel <= LogLevel.Warning ? Log(Console.Out, "WARN", message) : Task.CompletedTask;

        private async Task Log(TextWriter writer, string levelString, string message)
        {
            await _semaphore.WaitAsync();
            try
            {
                await writer.WriteLineAsync($"[{DateTime.Now:s}][{_prefix}][{levelString}] {message}");
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
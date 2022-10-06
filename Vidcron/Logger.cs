using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Vidcron
{
    public class Logger
    {
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private readonly LogLevel _logLevel;
        private readonly List<Message> _logMessages;

        private readonly string _prefix;

        public Logger(string prefix, LogLevel level)
        {
            _prefix = prefix;
            _logLevel = level;
            _logMessages = new List<Message>();
        }

        public IReadOnlyCollection<Message> LogMessages => _logMessages;

        public Task Debug(string message) => 
            Log(LogLevel.Debug, message);

        public Task Error(string message) =>
            Log(LogLevel.Error, message);

        public Task Info(string message) =>
            Log(LogLevel.Information, message);

        public Task Warn(string message) =>
            Log(LogLevel.Warning, message);

        private async Task Log(LogLevel level, string message)
        {
            if (_logLevel > level)
            {
                return;
            }
            
            var messageObj = new Message(_prefix, level, message);
            var writer = level <= LogLevel.Error ? Console.Error : Console.Out;

            await _semaphore.WaitAsync();
            try
            {
                await writer.WriteLineAsync(messageObj.ToString());
                _logMessages.Add(messageObj);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public class Message
        {
            public Message(string prefix, LogLevel level, string message)
            {
                Date = DateTime.Now;
                Level = level;
                MessageString = message.Trim();
                Prefix = prefix;
            }
            
            public DateTime Date { get; }
            
            public LogLevel Level { get; }
            
            public string MessageString { get; }
            
            public string Prefix { get; }

            public override string ToString()
            {
                string levelString;
                switch (Level)
                {
                    case LogLevel.Debug:
                        levelString = "DEBUG";
                        break;
                    case LogLevel.Error:
                        levelString = "ERROR";
                        break;
                    case LogLevel.Information:
                        levelString = "INFO";
                        break;
                    case LogLevel.Warning:
                        levelString = "WARN";
                        break;
                    default:
                        levelString = Level.ToString();
                        break;
                }

                return $"[{Date:s}][{Prefix}][{levelString}] {MessageString}";
            }
        }
    }
}
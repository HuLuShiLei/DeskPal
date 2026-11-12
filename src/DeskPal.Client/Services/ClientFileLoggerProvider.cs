using DeskPal.Client.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DeskPal.Client.Services;

/// <summary>Writes Information and above to <see cref="ClientLogFiles"/>. Debug collector traces stay out of the file.</summary>
public sealed class ClientFileLoggerProvider(ClientLogFiles files) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(files, categoryName);

    public void Dispose()
    {
    }

    private sealed class FileLogger(ClientLogFiles files, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter is null)
            {
                return;
            }

            var message = formatter(state, null);
            if (exception is not null)
            {
                message += $" ({exception.GetType().Name}: {exception.Message})";
            }

            files.Write(Level(logLevel), Short(category), message);
        }

        private static string Level(LogLevel logLevel) => logLevel switch
        {
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "INF",
        };

        private static string Short(string name)
        {
            var dot = name.LastIndexOf('.');
            return dot >= 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
        }
    }
}

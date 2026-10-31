using System.Text;
using Microsoft.Extensions.Logging;

namespace Odm.Agent;

/// <summary>
/// Writes log entries to <c>agent.log</c> and rotates when the maximum size is reached (agent.1.log … agent.N.log).
/// Errors while writing (e.g. missing permissions) must never affect the agent.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly long _maxBytes;
    private readonly int _keep;
    private readonly object _gate = new();
    private bool _disabled;

    public FileLoggerProvider(string directory, long maxBytes = 5 * 1024 * 1024, int keep = 5)
    {
        _directory = directory;
        _maxBytes = maxBytes;
        _keep = keep;
    }

    private string CurrentFile => Path.Combine(_directory, "agent.log");

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal void Write(string category, LogLevel level, string message, Exception? exception)
    {
        if (_disabled)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append(' ').Append(Short(level)).Append(' ')
            .Append(category).Append(": ").Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        line.AppendLine();

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                Rotate();
                File.AppendAllText(CurrentFile, line.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _disabled = true;
            }
        }
    }

    private void Rotate()
    {
        var info = new FileInfo(CurrentFile);
        if (!info.Exists || info.Length < _maxBytes)
        {
            return;
        }

        File.Delete(Path.Combine(_directory, $"agent.{_keep}.log"));
        for (var i = _keep - 1; i >= 1; i--)
        {
            var from = Path.Combine(_directory, $"agent.{i}.log");
            if (File.Exists(from))
            {
                File.Move(from, Path.Combine(_directory, $"agent.{i + 1}.log"));
            }
        }

        File.Move(CurrentFile, Path.Combine(_directory, "agent.1.log"));
    }

    private static string Short(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "---"
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(category, logLevel, formatter(state, exception), exception);
            }
        }
    }
}

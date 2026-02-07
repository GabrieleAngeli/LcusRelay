using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;

namespace LcusRelay.Tray.Logging;

/// <summary>
/// Logger minimale su file (append) + mirror su console se disponibile.
/// Thread-safe, con flush immediato per ridurre perdita log in shutdown improvvisi.
/// </summary>
public sealed class SimpleFileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, SimpleFileLogger> _loggers = new();
    private readonly object _sync = new();
    private readonly string _path;
    private readonly StreamWriter _writer;
    private readonly bool _mirrorConsole;

    public string FilePath => _path;

    public SimpleFileLoggerProvider(string path, bool mirrorConsole = true)
    {
        _path = EnsureWritablePath(path);
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);

        var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };

        _mirrorConsole = mirrorConsole;
        WriteInternal($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [Information] LcusRelay.Logging: Logger avviato su '{_path}'.");
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, c => new SimpleFileLogger(this, c));

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {category}: {message}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        WriteInternal(line);
    }

    private void WriteInternal(string line)
    {
        lock (_sync)
        {
            _writer.WriteLine(line);

            if (_mirrorConsole)
            {
                try { Console.WriteLine(line); } catch { /* no console */ }
            }

            try { System.Diagnostics.Debug.WriteLine(line); } catch { }
        }
    }

    private static string EnsureWritablePath(string preferredPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(preferredPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            using var _ = new FileStream(preferredPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            return preferredPath;
        }
        catch
        {
            var fallbackDir = Path.Combine(Path.GetTempPath(), "LcusRelay");
            Directory.CreateDirectory(fallbackDir);
            return Path.Combine(fallbackDir, "lcusrelay.log");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            try { _writer.Flush(); } catch { }
            try { _writer.Dispose(); } catch { }
        }
    }
}

internal sealed class SimpleFileLogger : ILogger
{
    private readonly SimpleFileLoggerProvider _provider;
    private readonly string _category;

    public SimpleFileLogger(SimpleFileLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var msg = formatter(state, exception);
        _provider.Write(logLevel, _category, msg, exception);
    }
}

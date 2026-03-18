using Microsoft.Extensions.Logging;

namespace ETLProject.Infrastructure;

public class EtlLoggerProvider : ILoggerProvider
{
    private readonly EtlLogBuffer _buffer;
    public EtlLoggerProvider(EtlLogBuffer buffer) => _buffer = buffer;

    public ILogger CreateLogger(string categoryName) => new EtlLogger(_buffer, categoryName);
    public void Dispose() { }
}

public class EtlLogger : ILogger
{
    private readonly EtlLogBuffer _buffer;
    private readonly string       _category;

    public EtlLogger(EtlLogBuffer buffer, string category)
    {
        _buffer   = buffer;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;

    public void Log<TState>(LogLevel level, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;

        var prefix = level switch
        {
            LogLevel.Warning  => "⚠️ WARN",
            LogLevel.Error    => "❌ ERROR",
            LogLevel.Critical => "🔴 CRITICAL",
            _                 => "ℹ️ INFO"
        };

        var msg = $"{prefix} | {_category.Split('.').Last()} | {formatter(state, exception)}";
        if (exception != null) msg += $" → {exception.Message}";
        _buffer.Add(msg);
    }
}
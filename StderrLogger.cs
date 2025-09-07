using Microsoft.Extensions.Logging;

namespace DecompilerServer;

public class StderrLogger(string category) : ILogger
{
    private readonly string _category = category;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        var message = formatter(state, exception);
        var output = $"STDERR [{DateTime.Now:HH:mm:ss}] {_category} [{logLevel}] {message}";
        if (exception != null) output += Environment.NewLine + exception;
        Console.Error.WriteLine(output);
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new NoopScope();
        private NoopScope() { }
        public void Dispose() { }
    }
}

public class StderrLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new StderrLogger(categoryName);
    public void Dispose() { GC.SuppressFinalize(this); }
}

using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DecompilerServer;

/// <summary>
/// Simple service locator for MCP tools to access registered services.
/// Thread-safe to support concurrent test execution and MCP tool calls.
/// </summary>
public static class ServiceLocator
{
    private static readonly ThreadLocal<IServiceProvider?> _threadLocalProvider = new ThreadLocal<IServiceProvider?>();
    private static volatile IServiceProvider? _globalProvider;
    private static readonly object _lock = new object();

    /// <summary>
    /// Sets the service provider. Uses global storage for production, thread-local for tests.
    /// </summary>
    public static void SetServiceProvider(IServiceProvider? serviceProvider)
    {
        // Always set thread-local for test compatibility.
        _threadLocalProvider.Value = serviceProvider;

        // Refresh the global provider whenever a non-null provider is supplied.
        // Tests create and dispose independent service providers, so "first write wins"
        // leaves the global fallback pointing at stale containers.
        if (serviceProvider != null)
        {
            lock (_lock)
            {
                _globalProvider = serviceProvider;
            }
        }
    }

    public static T GetRequiredService<T>() where T : notnull
    {
        // Try thread-local first (for tests)
        var provider = _threadLocalProvider.Value ?? _globalProvider;

        if (provider == null)
            throw new InvalidOperationException("Service provider not initialized for current thread");

        return provider.GetRequiredService<T>();
    }

    public static T? GetService<T>() where T : class
    {
        var provider = _threadLocalProvider.Value ?? _globalProvider;
        return provider?.GetService<T>();
    }

    public static DecompilerWorkspace? Workspace => GetService<DecompilerWorkspace>();

    public static AssemblyContextManager ContextManager => TryGetCurrentSession()?.ContextManager ?? GetRequiredService<AssemblyContextManager>();
    public static MemberResolver MemberResolver => TryGetCurrentSession()?.MemberResolver ?? GetRequiredService<MemberResolver>();
    public static DecompilerService DecompilerService => TryGetCurrentSession()?.DecompilerService ?? GetRequiredService<DecompilerService>();
    public static UsageAnalyzer UsageAnalyzer => TryGetCurrentSession()?.UsageAnalyzer ?? GetRequiredService<UsageAnalyzer>();
    public static InheritanceAnalyzer InheritanceAnalyzer => TryGetCurrentSession()?.InheritanceAnalyzer ?? GetRequiredService<InheritanceAnalyzer>();
    public static ResponseFormatter ResponseFormatter => GetRequiredService<ResponseFormatter>();

    private static DecompilerSession? TryGetCurrentSession()
    {
        var workspace = GetService<DecompilerWorkspace>();
        if (workspace != null && workspace.TryGetCurrentSession(out var session))
            return session;

        return null;
    }
}

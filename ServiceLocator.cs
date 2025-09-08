using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DecompilerServer;

/// <summary>
/// Simple service locator for MCP tools to access registered services.
/// Thread-safe to support concurrent test execution.
/// </summary>
public static class ServiceLocator
{
    private static readonly ThreadLocal<IServiceProvider?> _serviceProvider = new ThreadLocal<IServiceProvider?>();

    public static void SetServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider.Value = serviceProvider;
    }

    public static T GetRequiredService<T>() where T : notnull
    {
        if (_serviceProvider.Value == null)
            throw new InvalidOperationException("Service provider not initialized for current thread");

        return _serviceProvider.Value.GetRequiredService<T>();
    }

    public static AssemblyContextManager ContextManager => GetRequiredService<AssemblyContextManager>();
    public static MemberResolver MemberResolver => GetRequiredService<MemberResolver>();
    public static DecompilerService DecompilerService => GetRequiredService<DecompilerService>();
    public static UsageAnalyzer UsageAnalyzer => GetRequiredService<UsageAnalyzer>();
    public static InheritanceAnalyzer InheritanceAnalyzer => GetRequiredService<InheritanceAnalyzer>();
    public static ResponseFormatter ResponseFormatter => GetRequiredService<ResponseFormatter>();
}
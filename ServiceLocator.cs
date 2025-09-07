using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DecompilerServer;

/// <summary>
/// Simple service locator for MCP tools to access registered services
/// </summary>
public static class ServiceLocator
{
    private static IServiceProvider? _serviceProvider;

    public static void SetServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public static T GetRequiredService<T>() where T : notnull
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("Service provider not initialized");
        
        return _serviceProvider.GetRequiredService<T>();
    }

    public static AssemblyContextManager ContextManager => GetRequiredService<AssemblyContextManager>();
    public static MemberResolver MemberResolver => GetRequiredService<MemberResolver>();
    public static DecompilerService DecompilerService => GetRequiredService<DecompilerService>();
    public static UsageAnalyzer UsageAnalyzer => GetRequiredService<UsageAnalyzer>();
    public static InheritanceAnalyzer InheritanceAnalyzer => GetRequiredService<InheritanceAnalyzer>();
    public static ResponseFormatter ResponseFormatter => GetRequiredService<ResponseFormatter>();
}
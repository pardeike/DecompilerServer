using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class StatusTool
{
    [McpServerTool, Description("Get current server status, including assembly MVID and cache stats.")]
    public static string Status()
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var decompilerService = ServiceLocator.DecompilerService;
            var memberResolver = ServiceLocator.MemberResolver;

            var status = new ServerStatus
            {
                Loaded = contextManager.IsLoaded,
                Mvid = contextManager.Mvid,
                AssemblyPath = contextManager.AssemblyPath,
                StartedAtUnix = contextManager.LoadedAtUtc?.Ticks,
                Settings = contextManager.IsLoaded ? GetDecompilerSettings() : null,
                Stats = GetCacheStats(decompilerService, memberResolver),
                Indexes = contextManager.IsLoaded ? new IndexStatus
                {
                    Namespaces = contextManager.NamespaceCount,
                    Types = contextManager.TypeCount,
                    NameIndexReady = contextManager.TypeIndexReady,
                    StringLiteralIndexReady = false // TODO: Implement when needed
                } : null
            };

            return status;
        });
    }

    private static object GetDecompilerSettings()
    {
        // Return current decompiler settings
        return new
        {
            usingDeclarations = true,
            showXmlDocumentation = true,
            namedArguments = true
        };
    }

    private static object GetCacheStats(DecompilerService decompilerService, MemberResolver memberResolver)
    {
        var decompilerStats = decompilerService.GetCacheStats();
        var resolverStats = memberResolver.GetCacheStats();

        return new
        {
            sourceDocuments = decompilerStats.SourceDocuments,
            totalMemoryEstimate = decompilerStats.TotalMemoryEstimate,
            cachedResolutions = resolverStats.CachedResolutions,
            successfulResolutions = resolverStats.SuccessfulResolutions,
            failedResolutions = resolverStats.FailedResolutions
        };
    }
}
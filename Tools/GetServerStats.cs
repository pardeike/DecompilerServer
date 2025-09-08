using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class GetServerStatsTool
{
    [McpServerTool, Description("Basic health and timing info for the server.")]
    public static string GetServerStats()
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var decompilerService = ServiceLocator.DecompilerService;
            var memberResolver = ServiceLocator.MemberResolver;
            var usageAnalyzer = ServiceLocator.UsageAnalyzer;

            var stats = new
            {
                // Assembly info
                loaded = contextManager.IsLoaded,
                assemblyPath = contextManager.AssemblyPath,
                mvid = contextManager.Mvid,
                loadedAt = contextManager.LoadedAtUtc,

                // Index stats
                indexes = contextManager.GetIndexStats(),

                // Cache stats
                caches = new
                {
                    decompiler = decompilerService.GetCacheStats(),
                    memberResolver = memberResolver.GetCacheStats(),
                    usageAnalyzer = usageAnalyzer.GetCacheStats()
                },

                // Performance indicators
                performance = new
                {
                    typeIndexReady = contextManager.TypeIndexReady,
                    namespaceIndexReady = contextManager.NamespaceIndexReady,
                    memberIndexReady = contextManager.MemberIndexReady,
                    estimatedMemoryUsage = EstimateMemoryUsage(decompilerService, memberResolver, usageAnalyzer)
                }
            };

            return stats;
        });
    }

    private static long EstimateMemoryUsage(DecompilerService decompilerService, MemberResolver memberResolver, UsageAnalyzer usageAnalyzer)
    {
        var decompilerStats = decompilerService.GetCacheStats();
        var resolverStats = memberResolver.GetCacheStats();
        var usageStats = usageAnalyzer.GetCacheStats();

        // Rough estimate: source cache + resolution cache + usage cache
        return decompilerStats.TotalMemoryEstimate +
               (resolverStats.CachedResolutions * 100) + // 100 bytes per resolution estimate
               (usageStats.TotalUsageResults * 50) + // 50 bytes per usage result estimate
               (usageStats.TotalStringLiteralResults * 200); // 200 bytes per string literal estimate
    }
}

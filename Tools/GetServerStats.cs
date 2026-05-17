using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class GetServerStatsTool
{
    [McpServerTool, Description("Detailed cache, index, timing, and memory-estimate diagnostics for the current context or requested contextAlias. Use status/list_contexts for quick alias checks.")]
    public static string GetServerStats(string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute<object>(() =>
        {
            var workspace = ServiceLocator.Workspace;
            if (workspace != null)
            {
                DecompilerSession? session = null;
                if (!string.IsNullOrWhiteSpace(contextAlias))
                {
                    if (!workspace.TryGetSession(contextAlias, out session!))
                        throw new InvalidOperationException($"Context alias '{contextAlias}' is not loaded.");
                }
                else
                {
                    workspace.TryGetCurrentSession(out session!);
                }

                var loadedContexts = workspace.ListContexts().ToList();
                var workspaceStats = new
                {
                    loaded = session?.ContextManager.IsLoaded ?? false,
                    currentContextAlias = workspace.CurrentContextAlias,
                    contextAlias = session?.ContextAlias,
                    loadedContexts,
                    assemblyPath = session?.ContextManager.AssemblyPath,
                    mvid = session?.ContextManager.Mvid,
                    loadedAt = session?.ContextManager.LoadedAtUtc,
                    indexes = session?.ContextManager.GetIndexStats(),
                    caches = session == null ? null : new
                    {
                        decompiler = session.DecompilerService.GetCacheStats(),
                        memberResolver = session.MemberResolver.GetCacheStats(),
                        usageAnalyzer = session.UsageAnalyzer.GetCacheStats()
                    },
                    performance = session == null ? null : new
                    {
                        typeIndexReady = session.ContextManager.TypeIndexReady,
                        namespaceIndexReady = session.ContextManager.NamespaceIndexReady,
                        memberIndexReady = session.ContextManager.MemberIndexReady,
                        estimatedMemoryUsage = EstimateMemoryUsage(session.DecompilerService, session.MemberResolver, session.UsageAnalyzer)
                    }
                };

                return workspaceStats;
            }

            var contextManager = ServiceLocator.ContextManager;
            var decompilerService = ServiceLocator.DecompilerService;
            var memberResolver = ServiceLocator.MemberResolver;
            var usageAnalyzer = ServiceLocator.UsageAnalyzer;

            var legacyStats = new
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

            return legacyStats;
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

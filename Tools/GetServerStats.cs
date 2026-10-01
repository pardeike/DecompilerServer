using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class GetServerStatsTool
{
    [McpServerTool(ReadOnly = true), Description("Detailed cache, index, timing, and memory-estimate diagnostics for the current context or requested contextAlias. Use status/list_contexts for quick alias checks.")]
    public static string GetServerStats(string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute<object>(() =>
        {
            var workspace = ServiceLocator.Workspace;
            if (workspace != null)
            {
                using var sessionView = !string.IsNullOrWhiteSpace(contextAlias)
                    ? ToolSessionRouter.GetForContext(contextAlias)
                    : ToolSessionRouter.TryGetCurrentLoaded();

                var loadedContexts = workspace.ListContexts().ToList();
                var memory = workspace.GetMemoryStats(sessionView?.Lease?.Session);
                var workspaceStats = new
                {
                    loaded = sessionView?.ContextManager.IsLoaded ?? false,
                    currentContextAlias = workspace.CurrentContextAlias,
                    contextAlias = sessionView?.ContextAlias,
                    loadedContexts,
                    memory,
                    assemblyPath = sessionView?.ContextManager.AssemblyPath,
                    mvid = sessionView?.ContextManager.Mvid,
                    loadedAt = sessionView?.ContextManager.LoadedAtUtc,
                    indexes = sessionView?.ContextManager.GetIndexStats(),
                    caches = sessionView == null ? null : new
                    {
                        decompiler = sessionView.DecompilerService.GetCacheStats(),
                        memberResolver = sessionView.MemberResolver.GetCacheStats(),
                        usageAnalyzer = sessionView.UsageAnalyzer.GetCacheStats(),
                        search = sessionView.SearchService.GetSearchCacheStats()
                    },
                    performance = sessionView == null ? null : new
                    {
                        typeIndexReady = sessionView.ContextManager.TypeIndexReady,
                        namespaceIndexReady = sessionView.ContextManager.NamespaceIndexReady,
                        memberIndexReady = sessionView.ContextManager.MemberIndexReady,
                        estimatedMemoryUsage = EstimateMemoryUsage(sessionView.DecompilerService, sessionView.MemberResolver, sessionView.UsageAnalyzer) + sessionView.SearchService.GetSearchCacheStats().EstimatedBytes + sessionView.ContextManager.DependencyImageBytes,
                        dependencyImageBytes = sessionView.ContextManager.DependencyImageBytes
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
               resolverStats.EstimatedBytes +
               usageStats.EstimatedBytes;
    }
}

using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class StatusTool
{
    [McpServerTool, Description("Get current server status, including assembly MVID and cache stats.")]
    public static string Status()
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var workspace = ServiceLocator.Workspace;
            if (workspace != null)
            {
                var loadedContexts = workspace.ListContexts().ToList();
                var hasCurrent = workspace.TryGetCurrentSession(out var currentSession);
                var contextManager = hasCurrent ? currentSession.ContextManager : null;
                var status = new ServerStatus
                {
                    Loaded = loadedContexts.Count > 0,
                    CurrentContextAlias = workspace.CurrentContextAlias,
                    LoadedContexts = loadedContexts,
                    Mvid = contextManager?.Mvid,
                    AssemblyPath = contextManager?.AssemblyPath,
                    StartedAtUnix = contextManager?.LoadedAtUtc?.Ticks,
                    Settings = hasCurrent ? GetDecompilerSettings() : null,
                    Stats = hasCurrent ? GetCacheStats(currentSession.DecompilerService, currentSession.MemberResolver) : null,
                    Indexes = hasCurrent ? new IndexStatus
                    {
                        Namespaces = contextManager!.NamespaceCount,
                        Types = contextManager.TypeCount,
                        NameIndexReady = contextManager.TypeIndexReady,
                        StringLiteralIndexReady = GetStringLiteralIndexStatus(currentSession.UsageAnalyzer)
                    } : null
                };

                return status;
            }

            var legacyContextManager = ServiceLocator.ContextManager;
            var decompilerService = ServiceLocator.DecompilerService;
            var memberResolver = ServiceLocator.MemberResolver;
            var usageAnalyzer = ServiceLocator.UsageAnalyzer;

            var legacyStatus = new ServerStatus
            {
                Loaded = legacyContextManager.IsLoaded,
                CurrentContextAlias = null,
                LoadedContexts = null,
                Mvid = legacyContextManager.Mvid,
                AssemblyPath = legacyContextManager.AssemblyPath,
                StartedAtUnix = legacyContextManager.LoadedAtUtc?.Ticks,
                Settings = legacyContextManager.IsLoaded ? GetDecompilerSettings() : null,
                Stats = GetCacheStats(decompilerService, memberResolver),
                Indexes = legacyContextManager.IsLoaded ? new IndexStatus
                {
                    Namespaces = legacyContextManager.NamespaceCount,
                    Types = legacyContextManager.TypeCount,
                    NameIndexReady = legacyContextManager.TypeIndexReady,
                    StringLiteralIndexReady = GetStringLiteralIndexStatus(usageAnalyzer)
                } : null
            };

            return legacyStatus;
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
            rawContents = decompilerStats.RawContents,
            decompiledContents = decompilerStats.DecompiledContents,
            originalSourceContents = decompilerStats.OriginalSourceContents,
            sourceLinkContents = decompilerStats.SourceLinkContents,
            totalMemoryEstimate = decompilerStats.TotalMemoryEstimate,
            cachedResolutions = resolverStats.CachedResolutions,
            successfulResolutions = resolverStats.SuccessfulResolutions,
            failedResolutions = resolverStats.FailedResolutions
        };
    }

    private static bool GetStringLiteralIndexStatus(UsageAnalyzer usageAnalyzer)
    {
        // Consider string literal index ready if we have cached string literal queries
        var stats = usageAnalyzer.GetCacheStats();
        return stats.CachedStringLiteralQueries > 0;
    }
}

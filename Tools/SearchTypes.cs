using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class SearchTypesTool
{
    [McpServerTool, Description("Search types by name. Supports regex and filters. Modes: 'ids', 'discovery' (default), 'signatures', 'full'.")]
    public static string SearchTypes(
        string query,
        bool regex = false,
        string? namespaceFilter = null,
        bool includeNested = true,
        int limit = 50,
        string? cursor = null,
        string mode = "discovery")
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Create a SearchServiceBase instance to use the search functionality
            var searchService = new SearchService(contextManager, ServiceLocator.MemberResolver);
            var normalizedLimit = MemberSummaryModes.ClampLimit(limit, 50);
            var parsedMode = MemberSummaryModes.Parse(mode, MemberSummaryMode.Discovery);

            var result = searchService.SearchTypes(query, regex, namespaceFilter, includeNested, normalizedLimit, cursor);

            return MemberSummaryModes.Project(result, parsedMode);
        });
    }
}

/// <summary>
/// Concrete implementation of SearchServiceBase for use in tools
/// </summary>
internal class SearchService : SearchServiceBase
{
    public SearchService(AssemblyContextManager contextManager, MemberResolver memberResolver)
        : base(contextManager, memberResolver)
    {
    }
}

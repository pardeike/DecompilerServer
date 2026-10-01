using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class SearchTypesTool
{
    [McpServerTool(ReadOnly = true), Description("Search types by simple or qualified name. Supports regex and filters. Use search_symbols when unsure whether the name is a type or member.")]
    public static string SearchTypes(
        string query,
        bool regex = false,
        string? namespaceFilter = null,
        bool includeNested = true,
        int limit = 50,
        string? cursor = null,
        string mode = "discovery",
        string? contextAlias = null, bool includeCompilerGenerated = false)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            using var session = ToolSessionRouter.GetForContext(contextAlias);
            var contextManager = session.ContextManager;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Create a SearchServiceBase instance to use the search functionality
            var searchService = new SearchService(contextManager, session.MemberResolver);
            var normalizedLimit = MemberSummaryModes.ClampLimit(limit, 50);
            var parsedMode = MemberSummaryModes.Parse(mode, MemberSummaryMode.Discovery);

            var result = searchService.SearchTypes(query, regex, namespaceFilter, includeNested, normalizedLimit, cursor, includeCompilerGenerated);

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

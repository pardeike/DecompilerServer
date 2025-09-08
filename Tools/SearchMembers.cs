using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class SearchMembersTool
{
    [McpServerTool, Description("Search members (methods, ctors, properties, fields, events) with rich filters.")]
    public static string SearchMembers(string query, bool regex = false, string? namespaceFilter = null, string? declaringTypeFilter = null, string? attributeFilter = null, string? returnTypeFilter = null, string[]? paramTypeFilters = null, string? kind = null, string? accessibility = null, bool? isStatic = null, bool? isAbstract = null, bool? isVirtual = null, int? genericArity = null, int limit = 50, string? cursor = null)
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

            var result = searchService.SearchMembers(
                query,
                regex,
                namespaceFilter,
                declaringTypeFilter,
                attributeFilter,
                returnTypeFilter,
                paramTypeFilters,
                kind,
                accessibility,
                isStatic,
                isAbstract,
                isVirtual,
                genericArity,
                limit,
                cursor);

            return result;
        });
    }
}

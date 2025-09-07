using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class SearchTypesTool
{
    [McpServerTool, Description("Search types by name. Supports regex and filters.")]
    public static string SearchTypes(
        string query,
        bool regex = false,
        string? namespaceFilter = null,
        bool includeNested = true,
        int limit = 50,
        string? cursor = null)
    {
        /*
		Inputs: query, regex?, namespaceFilter?, includeNested?, limit, cursor.

		Behavior:
		- Case-insensitive substring by default; if regex=true use compiled Regex with timeout.
		- Filters: namespace, include nested types or not.
		- Return SearchResult<MemberSummary> with Kind="Type". Signature includes generic arity and base type.

		Helper methods to use:
		- AssemblyContextManager.IsLoaded to check if assembly is loaded
		- SearchServiceBase.SearchTypes() for main search logic
		- ResponseFormatter.TryExecute() for error handling
		- ResponseFormatter.SearchResult() for response formatting
		*/
        return "TODO";
    }
}
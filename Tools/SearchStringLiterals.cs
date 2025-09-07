using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class SearchStringLiteralsTool
{
    [McpServerTool, Description("Search string literals across code. Regex optional.")]
    public static string SearchStringLiterals(string pattern, bool regex = false, int limit = 100, string? cursor = null)
    {
        /*
		Behavior:
		- If string-literal index exists, query it. Else lazily scan methods in pages, decompiling or reading IL constants.
		- Return UsageRef entries with small code snippet containing the literal.

		Helper methods to use:
		- AssemblyContextManager.IsLoaded to check if assembly is loaded
		- UsageAnalyzer.FindStringLiterals() for IL-based string literal search
		- ResponseFormatter.TryExecute() for error handling
		- ResponseFormatter.SearchResult() for paginated response formatting
		*/
        return "TODO";
    }
}

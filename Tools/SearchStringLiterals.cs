using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class SearchStringLiteralsTool
{
    [McpServerTool, Description("Search string literals across code. Regex optional.")]
    public static string SearchStringLiterals(string pattern, bool regex = false, int limit = 100, string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var usageAnalyzer = ServiceLocator.UsageAnalyzer;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var stringLiterals = usageAnalyzer.FindStringLiterals(pattern, regex, limit, cursor);
            var literalsList = stringLiterals.ToList();

            // Calculate pagination info
            var startIndex = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
            {
                startIndex = cursorIndex;
            }

            var hasMore = literalsList.Count >= limit;
            var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

            // Convert StringLiteralReference to UsageRef-like format for consistency
            var usageRefs = literalsList.Select(lit => new
            {
                value = lit.Value,
                inMember = lit.ContainingMember,
                inType = lit.ContainingType,
                kind = "StringLiteral",
                line = lit.Line,
                snippet = $"\"{lit.Value}\""
            }).ToList();

            var result = new SearchResult<object>(usageRefs.Cast<object>().ToList(), hasMore, nextCursor, usageRefs.Count);

            return result;
        });
    }
}

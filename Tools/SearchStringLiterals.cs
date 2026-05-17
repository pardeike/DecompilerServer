using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class SearchStringLiteralsTool
{
    [McpServerTool, Description("Search string literals across code. Regex optional.")]
    public static string SearchStringLiterals(string pattern, bool regex = false, int limit = 100, string? cursor = null, string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var session = ToolSessionRouter.GetForContext(contextAlias);
            var contextManager = session.ContextManager;
            var usageAnalyzer = session.UsageAnalyzer;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var page = usageAnalyzer.FindStringLiteralsPage(pattern, regex, limit, cursor);

            // Convert StringLiteralReference to UsageRef-like format for consistency
            var usageRefs = page.Items.Select(lit => new
            {
                value = lit.Value,
                inMember = lit.ContainingMember,
                inType = lit.ContainingType,
                kind = "StringLiteral",
                line = lit.Line,
                snippet = $"\"{lit.Value}\""
            }).ToList();

            var result = new SearchResult<object>(usageRefs.Cast<object>().ToList(), page.HasMore, page.NextCursor, page.TotalEstimate);

            return result;
        });
    }
}

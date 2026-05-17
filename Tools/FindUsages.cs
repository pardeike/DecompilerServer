using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class FindUsagesTool
{
    [McpServerTool, Description("Find usages of a member across the assembly. Time-box and paginate.")]
    public static string FindUsages(string memberId, int limit = 100, string? cursor = null, string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var session = ToolSessionRouter.GetForMember(memberId, contextAlias);
            var contextManager = session.ContextManager;
            var usageAnalyzer = session.UsageAnalyzer;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            _ = ToolValidation.ResolveMemberOrThrow(session, memberId);
            var page = usageAnalyzer.FindUsagesPage(memberId, limit, cursor);
            var result = new SearchResult<UsageReference>(page.Items, page.HasMore, page.NextCursor, page.TotalEstimate);

            return result;
        });
    }
}

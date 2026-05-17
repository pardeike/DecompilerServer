using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class FindCallersTool
{
    [McpServerTool, Description("List direct callers of a method.")]
    public static string FindCallers(string methodId, int limit = 100, string? cursor = null, string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var session = ToolSessionRouter.GetForMember(methodId, contextAlias);
            var contextManager = session.ContextManager;
            var usageAnalyzer = session.UsageAnalyzer;
            var memberResolver = session.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            _ = ToolValidation.ResolveMethodOrThrow(session, methodId);

            var page = usageAnalyzer.FindCallersPage(methodId, limit, cursor);
            var result = new SearchResult<UsageReference>(page.Items, page.HasMore, page.NextCursor, page.TotalEstimate);

            return result;
        });
    }
}

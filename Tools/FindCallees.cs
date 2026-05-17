using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class FindCalleesTool
{
    [McpServerTool, Description("List direct callees invoked by a method, including targetMemberId when local, symbol, opcode, offset, and external/unresolved resolution status.")]
    public static string FindCallees(string methodId, int limit = 100, string? cursor = null, string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var session = ToolSessionRouter.GetForMember(methodId, contextAlias);
            var contextManager = session.ContextManager;
            var usageAnalyzer = session.UsageAnalyzer;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            _ = ToolValidation.ResolveMethodOrThrow(session, methodId);
            var page = usageAnalyzer.FindCalleesPage(methodId, limit, cursor);
            var result = new SearchResult<CalleeReference>(page.Items, page.HasMore, page.NextCursor, page.TotalEstimate);

            return result;
        });
    }
}

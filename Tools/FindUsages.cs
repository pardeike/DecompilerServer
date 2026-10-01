using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class FindUsagesTool
{
    [McpServerTool(ReadOnly = true), Description("Find usages. scope=assembly (default) or workspace scans currently loaded contexts; results identify searched contexts. Paginate with limit/cursor.")]
    public static string FindUsages(string memberId, int limit = 100, string? cursor = null, string? contextAlias = null, string scope = "assembly")
    {
        return ResponseFormatter.TryExecute(() =>
        {
            using var session = ToolSessionRouter.GetForMember(memberId, contextAlias);
            var contextManager = session.ContextManager;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var target = ToolValidation.ResolveMemberOrThrow(session, memberId);
            return ReferenceQuery.Find(session, target, memberId, callersOnly: false, scope, limit, cursor);
        });
    }
}

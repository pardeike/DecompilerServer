using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class FindCallersTool
{
    [McpServerTool(ReadOnly = true), Description("List direct callers. scope=assembly (default) or workspace scans currently loaded contexts; results identify searched contexts.")]
    public static string FindCallers(string methodId, int limit = 100, string? cursor = null, string? contextAlias = null, string scope = "assembly")
    {
        return ResponseFormatter.TryExecute(() =>
        {
            using var session = ToolSessionRouter.GetForMember(methodId, contextAlias);
            var contextManager = session.ContextManager;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var target = ToolValidation.ResolveMethodOrThrow(session, methodId);
            return ReferenceQuery.Find(session, target, methodId, callersOnly: true, scope, limit, cursor);
        });
    }
}

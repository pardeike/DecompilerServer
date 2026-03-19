using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class GetDecompiledSourceTool
{
    [McpServerTool, Description("Return source for a member. Type requests prefer embedded/local/SourceLink-backed original source when available; decompiled non-type members are returned as member-scoped snippets.")]
    public static string GetDecompiledSource(string memberId, bool includeHeader = true, string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var session = ToolSessionRouter.GetForMember(memberId, contextAlias);
            var contextManager = session.ContextManager;
            var decompilerService = session.DecompilerService;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var document = decompilerService.DecompileMember(memberId, includeHeader);
            return document;
        });
    }
}

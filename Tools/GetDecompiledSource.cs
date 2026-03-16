using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class GetDecompiledSourceTool
{
    [McpServerTool, Description("Return source for a member, preferring embedded/local/SourceLink-backed original source when available; otherwise decompile to C#. Caches document metadata.")]
    public static string GetDecompiledSource(string memberId, bool includeHeader = true)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var decompilerService = ServiceLocator.DecompilerService;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var document = decompilerService.DecompileMember(memberId, includeHeader);
            return document;
        });
    }
}

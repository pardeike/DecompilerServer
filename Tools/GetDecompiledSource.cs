using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class GetDecompiledSourceTool
{
    [McpServerTool, Description("Decompile a member to C#. Caches document and returns document metadata.")]
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

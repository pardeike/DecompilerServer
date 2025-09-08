using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class UnloadTool
{
    [McpServerTool, Description("Unload assembly and free all caches and indexes.")]
    public static string Unload()
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var decompilerService = ServiceLocator.DecompilerService;
            var memberResolver = ServiceLocator.MemberResolver;
            var usageAnalyzer = ServiceLocator.UsageAnalyzer;

            // Clear all caches before disposing context
            decompilerService.ClearCache();
            memberResolver.ClearCache();
            usageAnalyzer.ClearCache();

            // Dispose the assembly context which will:
            // - Dispose PEFile and resolver
            // - Clear all dictionaries and reset stats
            // - Reset lazy indexes
            contextManager.Dispose();

            return new { status = "ok" };
        });
    }
}
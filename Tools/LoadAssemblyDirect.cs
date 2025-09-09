using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class LoadAssemblyDirectTool
{
    [McpServerTool, Description("Load any .NET assembly directly by file path for decompilation and analysis.")]
    public static string LoadAssemblyDirect(
        string assemblyPath,
        string[]? additionalSearchDirs = null,
        bool rebuildIndex = true)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;

            // Load the assembly directly
            contextManager.LoadAssemblyDirect(assemblyPath, additionalSearchDirs);

            // Optionally warm up indexes for better performance
            if (rebuildIndex)
            {
                contextManager.WarmIndexes();
            }

            // Get assembly info for response
            var assemblyInfo = new AssemblyInfo
            {
                Mvid = contextManager.Mvid!,
                AssemblyPath = contextManager.AssemblyPath!,
                TypeCount = contextManager.TypeCount,
                MethodCount = EstimateMethodCount(contextManager),
                NamespaceCount = contextManager.NamespaceCount,
                Warmed = rebuildIndex
            };

            return assemblyInfo;
        });
    }

    private static int EstimateMethodCount(AssemblyContextManager contextManager)
    {
        // Quick estimate without building full index
        var types = contextManager.GetAllTypes();
        return types.Sum(t => t.Methods.Count());
    }
}
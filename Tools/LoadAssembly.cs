using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class LoadAssemblyTool
{
    [McpServerTool, Description("Load or reload the Assembly-CSharp.dll and build minimal indexes.")]
    public static string LoadAssembly(
        string gameDir,
        string assemblyFile = "Assembly-CSharp.dll",
        string[]? additionalSearchDirs = null,
        bool rebuildIndex = true)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;

            // Load the assembly with enhanced auto-detection
            contextManager.LoadAssembly(gameDir, assemblyFile, additionalSearchDirs);

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
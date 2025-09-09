using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class LoadAssemblyTool
{
    [McpServerTool, Description("Load any .NET assembly for decompilation and analysis. Use 'gameDir' for Unity projects with automatic path detection, or 'assemblyPath' for direct assembly loading.")]
    public static string LoadAssembly(
        string? gameDir = null,
        string? assemblyPath = null,
        string assemblyFile = "Assembly-CSharp.dll",
        string[]? additionalSearchDirs = null,
        bool rebuildIndex = true)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;

            // Validate parameters - exactly one of gameDir or assemblyPath must be provided
            if (gameDir != null && assemblyPath != null)
                throw new ArgumentException("Cannot specify both gameDir and assemblyPath. Use gameDir for Unity projects or assemblyPath for direct assembly loading.");

            if (gameDir == null && assemblyPath == null)
                throw new ArgumentException("Must specify either gameDir (for Unity projects) or assemblyPath (for direct assembly loading).");

            // Load the assembly using the appropriate method
            if (gameDir != null)
            {
                // Unity project loading with auto-detection
                contextManager.LoadAssembly(gameDir, assemblyFile, additionalSearchDirs);
            }
            else
            {
                // Direct assembly loading
                contextManager.LoadAssemblyDirect(assemblyPath!, additionalSearchDirs);
            }

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
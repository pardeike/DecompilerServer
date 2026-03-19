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
        bool rebuildIndex = true,
        string? contextAlias = null,
        bool makeCurrent = true)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var workspace = ServiceLocator.Workspace;

            if (workspace != null)
            {
                var loadedContext = workspace.LoadAssembly(new WorkspaceLoadRequest
                {
                    GameDir = gameDir,
                    AssemblyPath = assemblyPath,
                    AssemblyFile = assemblyFile,
                    AdditionalSearchDirs = additionalSearchDirs,
                    RebuildIndex = rebuildIndex,
                    ContextAlias = contextAlias,
                    MakeCurrent = makeCurrent
                });

                return new AssemblyInfo
                {
                    ContextAlias = loadedContext.ContextAlias,
                    Mvid = loadedContext.Mvid,
                    AssemblyPath = loadedContext.AssemblyPath,
                    TypeCount = loadedContext.TypeCount,
                    MethodCount = loadedContext.MethodCount,
                    NamespaceCount = loadedContext.NamespaceCount,
                    Warmed = rebuildIndex,
                    IsCurrent = loadedContext.IsCurrent
                };
            }

            var contextManager = ServiceLocator.ContextManager;
            var decompilerService = ServiceLocator.DecompilerService;
            var memberResolver = ServiceLocator.MemberResolver;
            var usageAnalyzer = ServiceLocator.UsageAnalyzer;

            // Validate parameters - exactly one of gameDir or assemblyPath must be provided
            if (gameDir != null && assemblyPath != null)
                throw new ArgumentException("Cannot specify both gameDir and assemblyPath. Use gameDir for Unity projects or assemblyPath for direct assembly loading.");

            if (gameDir == null && assemblyPath == null)
                throw new ArgumentException("Must specify either gameDir (for Unity projects) or assemblyPath (for direct assembly loading).");

            // Drop singleton caches eagerly before switching contexts.
            decompilerService.ClearCache();
            memberResolver.ClearCache();
            usageAnalyzer.ClearCache();

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
                ContextAlias = contextAlias,
                Mvid = contextManager.Mvid!,
                AssemblyPath = contextManager.AssemblyPath!,
                TypeCount = contextManager.TypeCount,
                MethodCount = EstimateMethodCount(contextManager),
                NamespaceCount = contextManager.NamespaceCount,
                Warmed = rebuildIndex,
                IsCurrent = true
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

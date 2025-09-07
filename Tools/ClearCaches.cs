using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class ClearCachesTool
{
    [McpServerTool, Description("Clear caches and indexes. Scope: 'all' | 'source' | 'usages' | 'attributes'.")]
    public static string ClearCaches(string scope = "all")
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var decompilerService = ServiceLocator.DecompilerService;
            var memberResolver = ServiceLocator.MemberResolver;
            
            var clearedItems = new List<string>();

            switch (scope.ToLowerInvariant())
            {
                case "all":
                    decompilerService.ClearCache();
                    memberResolver.ClearCache();
                    clearedItems.AddRange(new[] { "source", "resolutions" });
                    break;
                    
                case "source":
                    decompilerService.ClearCache();
                    clearedItems.Add("source");
                    break;
                    
                case "resolutions":
                    memberResolver.ClearCache();
                    clearedItems.Add("resolutions");
                    break;
                    
                default:
                    throw new ArgumentException($"Invalid scope: {scope}. Use 'all', 'source', or 'resolutions'");
            }

            return new
            {
                cleared = string.Join(", ", clearedItems),
                scope = scope
            };
        });
    }
}
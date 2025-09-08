using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class ClearCachesTool
{
    [McpServerTool, Description("Clear caches and indexes. Scope: 'all' | 'source' | 'resolutions' | 'usage'.")]
    public static string ClearCaches(string scope = "all")
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var decompilerService = ServiceLocator.DecompilerService;
            var memberResolver = ServiceLocator.MemberResolver;
            var usageAnalyzer = ServiceLocator.UsageAnalyzer;

            var clearedItems = new List<string>();

            switch (scope.ToLowerInvariant())
            {
                case "all":
                    decompilerService.ClearCache();
                    memberResolver.ClearCache();
                    usageAnalyzer.ClearCache();
                    clearedItems.AddRange(new[] { "source", "resolutions", "usage" });
                    break;

                case "source":
                    decompilerService.ClearCache();
                    clearedItems.Add("source");
                    break;

                case "resolutions":
                    memberResolver.ClearCache();
                    clearedItems.Add("resolutions");
                    break;

                case "usage":
                case "usages":
                    usageAnalyzer.ClearCache();
                    clearedItems.Add("usage");
                    break;

                default:
                    throw new ArgumentException($"Invalid scope: {scope}. Use 'all', 'source', 'resolutions', or 'usage'");
            }

            return new
            {
                cleared = string.Join(", ", clearedItems),
                scope = scope
            };
        });
    }
}
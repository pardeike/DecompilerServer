using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using System.Diagnostics;

namespace DecompilerServer;

[McpServerToolType]
public static class WarmIndexTool
{
    [McpServerTool, Description("Optionally precompute heavier indexes (string literals, attribute hits). Time-boxed.")]
    public static string WarmIndex(bool deep = false, double maxSeconds = 5.0)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var contextManager = ServiceLocator.ContextManager;
            var built = new List<string>();

            if (!contextManager.IsLoaded)
                throw new InvalidOperationException("No assembly loaded");

            // Always warm basic indexes
            contextManager.WarmIndexes();
            built.Add("basic");

            if (deep && stopwatch.Elapsed.TotalSeconds < maxSeconds)
            {
                // Implement deep indexing for string literals and attributes
                var usageAnalyzer = ServiceLocator.UsageAnalyzer;

                // Pre-warm string literal searches by running a quick search
                try
                {
                    usageAnalyzer.FindStringLiterals("", regex: false, limit: 1);
                    built.Add("string-literals");
                }
                catch
                {
                    // Ignore errors during warm-up
                }

                // Pre-warm usage analysis by analyzing a few types
                try
                {
                    var types = contextManager.GetAllTypes().Take(5);
                    foreach (var type in types)
                    {
                        if (stopwatch.Elapsed.TotalSeconds >= maxSeconds) break;

                        var memberId = ServiceLocator.MemberResolver.GenerateMemberId(type);
                        usageAnalyzer.FindUsages(memberId, limit: 1);
                    }
                    built.Add("usage-analysis");
                }
                catch
                {
                    // Ignore errors during warm-up
                }

                built.Add("deep-indexing");
            }

            stopwatch.Stop();

            return new
            {
                deepRequested = deep,
                elapsedMs = (int)stopwatch.ElapsedMilliseconds,
                built = built.ToArray(),
                indexStats = contextManager.GetIndexStats()
            };
        });
    }
}
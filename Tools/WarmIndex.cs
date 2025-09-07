using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using System.Diagnostics;

namespace DecompilerServer;

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
                // TODO: Implement deep indexing for string literals and attributes
                // For now, just warm what we have
                built.Add("deep-placeholder");
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
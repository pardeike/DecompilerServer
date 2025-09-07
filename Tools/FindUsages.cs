using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class FindUsagesTool
{
    [McpServerTool, Description("Find usages of a member across the assembly. Time-box and paginate.")]
    public static string FindUsages(string memberId, int limit = 100, string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var usageAnalyzer = ServiceLocator.GetRequiredService<UsageAnalyzer>();

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var usages = usageAnalyzer.FindUsages(memberId, limit, cursor);
            var usageList = usages.ToList();

            // Calculate pagination info
            var startIndex = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
            {
                startIndex = cursorIndex;
            }

            var hasMore = usageList.Count >= limit;
            var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

            var result = new SearchResult<UsageReference>
            {
                Items = usageList,
                HasMore = hasMore,
                NextCursor = nextCursor,
                TotalEstimate = usageList.Count
            };

            return result;
        });
    }
}

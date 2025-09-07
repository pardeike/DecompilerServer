using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class FindCalleesTool
{
    [McpServerTool, Description("List direct callees invoked by a method.")]
    public static string FindCallees(string methodId, int limit = 100, string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var usageAnalyzer = ServiceLocator.UsageAnalyzer;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var callees = usageAnalyzer.FindCallees(methodId, limit, cursor);
            var calleeList = callees.ToList();

            // Calculate pagination info
            var startIndex = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
            {
                startIndex = cursorIndex;
            }

            var hasMore = calleeList.Count >= limit;
            var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

            var result = new SearchResult<UsageReference>
            {
                Items = calleeList,
                HasMore = hasMore,
                NextCursor = nextCursor,
                TotalEstimate = calleeList.Count
            };

            return result;
        });
    }
}

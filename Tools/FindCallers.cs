using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class FindCallersTool
{
    [McpServerTool, Description("List direct callers of a method.")]
    public static string FindCallers(string methodId, int limit = 100, string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var usageAnalyzer = ServiceLocator.UsageAnalyzer;
            var memberResolver = ServiceLocator.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Validate that the memberId is indeed a method
            var method = memberResolver.ResolveMethod(methodId);
            if (method == null)
            {
                throw new ArgumentException($"Invalid method ID or member is not a method: {methodId}");
            }

            var callers = usageAnalyzer.FindCallers(methodId, limit, cursor);
            var callersList = callers.ToList();

            // Calculate pagination info
            var startIndex = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
            {
                startIndex = cursorIndex;
            }

            var hasMore = callersList.Count >= limit;
            var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

            var result = new SearchResult<UsageReference>(callersList, hasMore, nextCursor, callersList.Count);

            return result;
        });
    }
}

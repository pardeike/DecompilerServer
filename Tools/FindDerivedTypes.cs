using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class FindDerivedTypesTool
{
    [McpServerTool, Description("Find derived types of a base class. Optionally include indirect.")]
    public static string FindDerivedTypes(string baseTypeId, bool transitive = true, int limit = 100, string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var inheritanceAnalyzer = ServiceLocator.GetRequiredService<InheritanceAnalyzer>();

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var derivedTypes = inheritanceAnalyzer.FindDerivedTypes(baseTypeId, limit, cursor);

            // Calculate pagination info
            var totalDerived = derivedTypes.ToList();
            var startIndex = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
            {
                startIndex = cursorIndex;
            }

            var hasMore = totalDerived.Count >= limit;
            var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

            var result = new SearchResult<MemberSummary>
            {
                Items = totalDerived,
                HasMore = hasMore,
                NextCursor = nextCursor,
                TotalEstimate = totalDerived.Count
            };

            return result;
        });
    }
}

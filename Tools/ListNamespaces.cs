using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class ListNamespacesTool
{
    [McpServerTool, Description("List namespaces. Optional prefix filter and pagination.")]
    public static string ListNamespaces(string? prefix = null, int limit = 100, string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Get all namespaces
            var namespaces = contextManager.GetNamespaces();

            // Apply prefix filter if provided
            if (!string.IsNullOrEmpty(prefix))
            {
                namespaces = namespaces.Where(ns =>
                    ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }

            // Sort for consistent ordering
            var sortedNamespaces = namespaces.OrderBy(ns => ns).ToList();

            // Apply pagination
            var startIndex = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
            {
                startIndex = cursorIndex;
            }

            var pageItems = sortedNamespaces
                .Skip(startIndex)
                .Take(limit)
                .Select(ns => new MemberHandle($"N:{ns}", ns, "Namespace"))
                .ToList();

            var hasMore = startIndex + limit < sortedNamespaces.Count;
            var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

            var result = new SearchResult<MemberHandle>(pageItems, hasMore, nextCursor, sortedNamespaces.Count);

            return result;
        });
    }
}

/// <summary>
/// Simple handle for members that don't need full summary information
/// </summary>
public record MemberHandle(string MemberId, string Name, string Kind);
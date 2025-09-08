using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
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

              var derivedTypes = inheritanceAnalyzer.FindDerivedTypes(baseTypeId, limit, cursor, transitive);

              // Calculate pagination info
              var totalDerived = derivedTypes.ToList();
              var startIndex = 0;
              if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
              {
                  startIndex = cursorIndex;
              }

              var hasMore = totalDerived.Count >= limit;
              var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

              var result = new SearchResult<MemberSummary>(totalDerived, hasMore, nextCursor, totalDerived.Count);

              return result;
          });
    }
}

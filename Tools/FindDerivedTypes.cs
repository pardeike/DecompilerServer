using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class FindDerivedTypesTool
{
    [McpServerTool, Description("Find derived types of a base class. Optionally include indirect.")]
    public static string FindDerivedTypes(string baseTypeId, bool transitive = true, int limit = 100, string? cursor = null, string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
          {
              if (limit <= 0)
                  throw new ArgumentException("limit must be greater than 0.", nameof(limit));

              var session = ToolSessionRouter.GetForMember(baseTypeId, contextAlias);
              var contextManager = session.ContextManager;
              var inheritanceAnalyzer = session.InheritanceAnalyzer;

              if (!contextManager.IsLoaded)
              {
                  throw new InvalidOperationException("No assembly loaded");
              }

              _ = ToolValidation.ResolveTypeOrThrow(session, baseTypeId);
              var allDerivedTypes = inheritanceAnalyzer.FindDerivedTypes(baseTypeId, int.MaxValue, null, transitive).ToList();
              var startIndex = ParseCursor(cursor);
              var pageItems = allDerivedTypes.Skip(startIndex).Take(limit).ToList();
              var hasMore = startIndex + limit < allDerivedTypes.Count;
              var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

              var result = new SearchResult<MemberSummary>(pageItems, hasMore, nextCursor, allDerivedTypes.Count);

              return result;
          });
    }

    private static int ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;

        if (!int.TryParse(cursor, out var startIndex) || startIndex < 0)
            throw new ArgumentException("cursor must be a non-negative integer.", nameof(cursor));

        return startIndex;
    }
}

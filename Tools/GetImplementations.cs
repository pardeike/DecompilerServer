using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class GetImplementationsTool
{
    [McpServerTool, Description("Find implementations of an interface or interface method.")]
    public static string GetImplementations(string interfaceTypeOrMethodId, int limit = 100, string? cursor = null, string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            if (limit <= 0)
                throw new ArgumentException("limit must be greater than 0.", nameof(limit));

            var session = ToolSessionRouter.GetForMember(interfaceTypeOrMethodId, contextAlias);
            var contextManager = session.ContextManager;
            var inheritanceAnalyzer = session.InheritanceAnalyzer;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Try to resolve as a member first to determine what we're dealing with
            var member = ToolValidation.ResolveMemberOrThrow(session, interfaceTypeOrMethodId);

            IEnumerable<MemberSummary> implementations;

            if (member is ICSharpCode.Decompiler.TypeSystem.IType)
            {
                // It's a type - find all types implementing this interface
                implementations = inheritanceAnalyzer.FindImplementors(interfaceTypeOrMethodId, int.MaxValue, null);
            }
            else if (member is ICSharpCode.Decompiler.TypeSystem.IMethod)
            {
                // It's a method - find concrete implementations
                implementations = inheritanceAnalyzer.FindMethodImplementations(interfaceTypeOrMethodId, int.MaxValue, null);
            }
            else
            {
                throw new ArgumentException($"Member must be an interface type or interface method: {interfaceTypeOrMethodId}");
            }

            var implementationsList = implementations.ToList();

            var startIndex = ParseCursor(cursor);
            var pageItems = implementationsList.Skip(startIndex).Take(limit).ToList();
            var hasMore = startIndex + limit < implementationsList.Count;
            var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

            var result = new SearchResult<MemberSummary>(pageItems, hasMore, nextCursor,
                implementationsList.Count);

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

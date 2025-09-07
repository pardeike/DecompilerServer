using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class GetImplementationsTool
{
    [McpServerTool, Description("Find implementations of an interface or interface method.")]
    public static string GetImplementations(string interfaceTypeOrMethodId, int limit = 100, string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;
            var inheritanceAnalyzer = ServiceLocator.InheritanceAnalyzer;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Try to resolve as a member first to determine what we're dealing with
            var member = memberResolver.ResolveMember(interfaceTypeOrMethodId);
            if (member == null)
            {
                throw new ArgumentException($"Invalid member ID: {interfaceTypeOrMethodId}");
            }

            IEnumerable<MemberSummary> implementations;

            if (member is ICSharpCode.Decompiler.TypeSystem.IType type)
            {
                // It's a type - find all types implementing this interface
                implementations = inheritanceAnalyzer.FindImplementors(interfaceTypeOrMethodId, limit, cursor);
            }
            else if (member is ICSharpCode.Decompiler.TypeSystem.IMethod method)
            {
                // It's a method - find concrete implementations
                var overrides = inheritanceAnalyzer.GetOverrides(interfaceTypeOrMethodId);

                // Apply pagination manually for method overrides
                var methodStartIndex = 0;
                if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var methodCursorIndex))
                {
                    methodStartIndex = methodCursorIndex;
                }

                implementations = overrides.Skip(methodStartIndex).Take(limit);
            }
            else
            {
                throw new ArgumentException($"Member must be an interface type or interface method: {interfaceTypeOrMethodId}");
            }

            var implementationsList = implementations.ToList();

            // Calculate pagination info
            var startIndex = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
            {
                startIndex = cursorIndex;
            }

            var hasMore = implementationsList.Count >= limit;
            var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

            var result = new SearchResult<MemberSummary>(implementationsList, hasMore, nextCursor,
                implementationsList.Count);

            return result;
        });
    }
}

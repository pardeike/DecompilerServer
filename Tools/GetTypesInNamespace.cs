using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class GetTypesInNamespaceTool
{
    [McpServerTool, Description("Get all types inside a namespace. Optional deep traversal for child namespaces.")]
    public static string GetTypesInNamespace(string ns, bool deep = false, int limit = 100, string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Get all types
            var allTypes = contextManager.GetAllTypes();

            // Filter by namespace
            IEnumerable<ICSharpCode.Decompiler.TypeSystem.ITypeDefinition> filteredTypes;
            if (deep)
            {
                // Include types in the namespace or any child namespace
                filteredTypes = allTypes.Where(type =>
                    type.Namespace == ns ||
                    type.Namespace.StartsWith(ns + ".", StringComparison.Ordinal));
            }
            else
            {
                // Exact namespace match only
                filteredTypes = allTypes.Where(type => type.Namespace == ns);
            }

            // Sort for consistent ordering
            var sortedTypes = filteredTypes.OrderBy(type => type.FullName).ToList();

            // Apply pagination
            var startIndex = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
            {
                startIndex = cursorIndex;
            }

            var pageItems = sortedTypes
                .Skip(startIndex)
                .Take(limit)
                .Select(type => new MemberSummary
                {
                    MemberId = memberResolver.GenerateMemberId(type),
                    Name = type.Name,
                    FullName = type.FullName,
                    Kind = "Type",
                    DeclaringType = type.DeclaringType?.FullName,
                    Namespace = type.Namespace,
                    Signature = memberResolver.GetMemberSignature(type),
                    Accessibility = type.Accessibility.ToString(),
                    IsStatic = type.IsStatic,
                    IsAbstract = type.IsAbstract
                })
                .ToList();

            var hasMore = startIndex + limit < sortedTypes.Count;
            var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

            var result = new SearchResult<MemberSummary>(pageItems, hasMore, nextCursor, sortedTypes.Count);

            return result;
        });
    }
}

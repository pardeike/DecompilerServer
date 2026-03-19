using System.ComponentModel;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;
using ModelContextProtocol.Server;

namespace DecompilerServer;

[McpServerToolType]
public static class CompareContextsTool
{
    [McpServerTool, Description("Compare the type surface of two loaded contexts with structured summary output.")]
    public static string CompareContexts(
        string leftContextAlias,
        string rightContextAlias,
        string? namespaceFilter = null,
        bool deep = false,
        bool includeUnchanged = false,
        bool includeCompilerGenerated = false,
        int limit = 200,
        string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            if (limit <= 0)
                throw new ArgumentException("limit must be greater than 0.", nameof(limit));

            var left = ToolSessionRouter.GetForContext(leftContextAlias);
            var right = ToolSessionRouter.GetForContext(rightContextAlias);

            var leftTypes = GetFilteredTypes(left.ContextManager, namespaceFilter, deep, includeCompilerGenerated)
                .ToDictionary(type => type.FullName, StringComparer.Ordinal);
            var rightTypes = GetFilteredTypes(right.ContextManager, namespaceFilter, deep, includeCompilerGenerated)
                .ToDictionary(type => type.FullName, StringComparer.Ordinal);

            var summary = new ContextComparisonSummary
            {
                TotalLeftTypes = leftTypes.Count,
                TotalRightTypes = rightTypes.Count
            };

            var items = new List<ContextTypeDiffItem>();
            var typeNames = leftTypes.Keys.Union(rightTypes.Keys).OrderBy(name => name, StringComparer.Ordinal);

            foreach (var typeName in typeNames)
            {
                leftTypes.TryGetValue(typeName, out var leftType);
                rightTypes.TryGetValue(typeName, out var rightType);

                var diff = TypeSurfaceComparer.Compare(leftType, rightType, left.MemberResolver, right.MemberResolver);
                var status = DetermineStatus(diff);

                switch (status)
                {
                    case "added":
                        summary.AddedTypes++;
                        break;
                    case "removed":
                        summary.RemovedTypes++;
                        break;
                    case "changed":
                        summary.ChangedTypes++;
                        break;
                    default:
                        summary.UnchangedTypes++;
                        break;
                }

                if (!includeUnchanged && status == "unchanged")
                    continue;

                items.Add(new ContextTypeDiffItem
                {
                    Status = status,
                    TypeName = typeName,
                    Namespace = leftType?.Namespace ?? rightType?.Namespace ?? string.Empty,
                    LeftExists = diff.LeftExists,
                    RightExists = diff.RightExists,
                    MemberDelta = status == "changed"
                        ? new TypeMemberDeltaSummary
                        {
                            AddedOrRemovedMembers = diff.AddedMembers.Count + diff.RemovedMembers.Count,
                            ChangedMembers = diff.ChangedMembers.Count
                        }
                        : null
                });
            }

            var startIndex = 0;
            if (!string.IsNullOrWhiteSpace(cursor) && !int.TryParse(cursor, out startIndex))
                throw new ArgumentException("cursor must be an integer offset.", nameof(cursor));

            var pagedItems = items.Skip(startIndex).Take(limit).ToList();
            var hasMore = startIndex + limit < items.Count;

            return new ContextComparisonResult
            {
                LeftContextAlias = left.ContextAlias ?? leftContextAlias,
                RightContextAlias = right.ContextAlias ?? rightContextAlias,
                CompareMode = "surface",
                NamespaceFilter = namespaceFilter,
                Deep = deep,
                IncludeUnchanged = includeUnchanged,
                IncludeCompilerGenerated = includeCompilerGenerated,
                Summary = summary,
                Items = pagedItems,
                HasMore = hasMore,
                NextCursor = hasMore ? (startIndex + limit).ToString() : null,
                TotalEstimate = items.Count
            };
        });
    }

    private static IEnumerable<ITypeDefinition> GetFilteredTypes(
        AssemblyContextManager contextManager,
        string? namespaceFilter,
        bool deep,
        bool includeCompilerGenerated)
    {
        if (!contextManager.IsLoaded)
            throw new InvalidOperationException("No assembly loaded");

        IEnumerable<ITypeDefinition> types = contextManager.GetAllTypes();

        if (!includeCompilerGenerated)
        {
            types = types.Where(type => !TypeSurfaceComparer.IsCompilerGenerated(type));
        }

        if (string.IsNullOrWhiteSpace(namespaceFilter))
            return types;

        return deep
            ? types.Where(type =>
                type.Namespace == namespaceFilter ||
                type.Namespace.StartsWith(namespaceFilter + ".", StringComparison.Ordinal))
            : types.Where(type => type.Namespace == namespaceFilter);
    }

    private static string DetermineStatus(TypeSurfaceDiff diff)
    {
        if (!diff.LeftExists && diff.RightExists)
            return "added";

        if (diff.LeftExists && !diff.RightExists)
            return "removed";

        return diff.AddedMembers.Count > 0 || diff.RemovedMembers.Count > 0 || diff.ChangedMembers.Count > 0
            ? "changed"
            : "unchanged";
    }
}

public sealed record ContextComparisonResult
{
    public required string LeftContextAlias { get; init; }
    public required string RightContextAlias { get; init; }
    public required string CompareMode { get; init; }
    public string? NamespaceFilter { get; init; }
    public bool Deep { get; init; }
    public bool IncludeUnchanged { get; init; }
    public bool IncludeCompilerGenerated { get; init; }
    public required ContextComparisonSummary Summary { get; init; }
    public required List<ContextTypeDiffItem> Items { get; init; }
    public bool HasMore { get; init; }
    public string? NextCursor { get; init; }
    public int TotalEstimate { get; init; }
}

public sealed record ContextComparisonSummary
{
    public int TotalLeftTypes { get; init; }
    public int TotalRightTypes { get; init; }
    public int AddedTypes { get; set; }
    public int RemovedTypes { get; set; }
    public int ChangedTypes { get; set; }
    public int UnchangedTypes { get; set; }
}

public sealed record ContextTypeDiffItem
{
    public required string Status { get; init; }
    public required string TypeName { get; init; }
    public required string Namespace { get; init; }
    public bool LeftExists { get; init; }
    public bool RightExists { get; init; }
    public TypeMemberDeltaSummary? MemberDelta { get; init; }
}

public sealed record TypeMemberDeltaSummary
{
    public int AddedOrRemovedMembers { get; init; }
    public int ChangedMembers { get; init; }
}

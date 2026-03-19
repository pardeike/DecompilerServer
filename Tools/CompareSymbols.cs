using System.ComponentModel;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;
using ModelContextProtocol.Server;

namespace DecompilerServer;

[McpServerToolType]
public static class CompareSymbolsTool
{
    [McpServerTool, Description("Compare a type or member between two loaded contexts using a compact, kind-aware diff. compareMode: 'surface' for all kinds; methods also support 'body' (alias 'source'). Member symbols accept 'Namespace.Type:MemberName' or 'Namespace.Type.MemberName'.")]
    public static string CompareSymbols(
        string leftContextAlias,
        string rightContextAlias,
        string symbol,
        string symbolKind = "auto",
        string compareMode = "surface")
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var left = ToolSessionRouter.GetForContext(leftContextAlias);
            var right = ToolSessionRouter.GetForContext(rightContextAlias);

            var normalizedKind = NormalizeSymbolKind(symbolKind, symbol);
            var normalizedCompareMode = NormalizeCompareMode(compareMode);

            return (normalizedKind, normalizedCompareMode) switch
            {
                ("type", "surface") => CompareType(left, right, symbol),
                ("method", "surface") => CompareMember(left, right, symbol, "method", normalizedCompareMode),
                ("method", "body") => CompareMethodBody(left, right, symbol),
                ("field", "surface") => CompareMember(left, right, symbol, "field", normalizedCompareMode),
                ("property", "surface") => CompareMember(left, right, symbol, "property", normalizedCompareMode),
                ("event", "surface") => CompareMember(left, right, symbol, "event", normalizedCompareMode),
                ("type", _) => throw new ArgumentException($"compareMode '{compareMode}' is not supported for symbolKind '{normalizedKind}'. Types support only 'surface'."),
                (_, "body") => throw new ArgumentException($"compareMode '{compareMode}' is only supported for methods. Member symbols accept 'Namespace.Type:MemberName' or 'Namespace.Type.MemberName'."),
                _ => throw new ArgumentException($"Unsupported symbolKind '{symbolKind}' or compareMode '{compareMode}'.")
            };
        });
    }

    private static object CompareType(ToolSessionView left, ToolSessionView right, string typeName)
    {
        var leftType = FindType(left, typeName);
        var rightType = FindType(right, typeName);

        var result = new TypeComparisonResult
        {
            LeftContextAlias = left.ContextAlias ?? "current",
            RightContextAlias = right.ContextAlias ?? "current",
            Symbol = typeName,
            SymbolKind = "Type",
            CompareMode = "surface",
            LeftExists = leftType != null,
            RightExists = rightType != null,
            AddedMembers = new List<ComparedMemberSummary>(),
            RemovedMembers = new List<ComparedMemberSummary>(),
            ChangedMembers = new List<ChangedMemberSummary>()
        };

        var diff = TypeSurfaceComparer.Compare(leftType, rightType, left.MemberResolver, right.MemberResolver);
        result.AddedMembers.AddRange(diff.AddedMembers);
        result.RemovedMembers.AddRange(diff.RemovedMembers);
        result.ChangedMembers.AddRange(diff.ChangedMembers);

        return result;
    }

    private static object CompareMember(ToolSessionView left, ToolSessionView right, string symbol, string expectedKind, string compareMode)
    {
        var parsed = ParseMemberSymbol(symbol);
        var leftType = FindType(left, parsed.TypeName);
        var rightType = FindType(right, parsed.TypeName);
        var leftMember = leftType != null ? FindMember(leftType, parsed.MemberName, expectedKind) : null;
        var rightMember = rightType != null ? FindMember(rightType, parsed.MemberName, expectedKind) : null;

        return new MemberComparisonResult
        {
            LeftContextAlias = left.ContextAlias ?? "current",
            RightContextAlias = right.ContextAlias ?? "current",
            Symbol = symbol,
            SymbolKind = TypeSurfaceComparer.ToDisplayKind(expectedKind),
            CompareMode = compareMode,
            LeftExists = leftMember != null,
            RightExists = rightMember != null,
            LeftSignature = leftMember != null ? left.MemberResolver.GetMemberSignature(leftMember) : null,
            RightSignature = rightMember != null ? right.MemberResolver.GetMemberSignature(rightMember) : null,
            SignatureChanged = leftMember != null && rightMember != null &&
                !string.Equals(left.MemberResolver.GetMemberSignature(leftMember), right.MemberResolver.GetMemberSignature(rightMember), StringComparison.Ordinal)
        };
    }

    private static object CompareMethodBody(ToolSessionView left, ToolSessionView right, string symbol)
    {
        var parsed = ParseMemberSymbol(symbol);
        var leftType = FindType(left, parsed.TypeName);
        var rightType = FindType(right, parsed.TypeName);
        var leftMethod = leftType != null ? FindMember(leftType, parsed.MemberName, "method") as IMethod : null;
        var rightMethod = rightType != null ? FindMember(rightType, parsed.MemberName, "method") as IMethod : null;

        string? leftSource = null;
        string? rightSource = null;

        if (leftMethod != null)
        {
            var leftMemberId = left.MemberResolver.GenerateMemberId(leftMethod);
            leftSource = left.DecompilerService.DecompileEntitySnippet(leftMemberId);
        }

        if (rightMethod != null)
        {
            var rightMemberId = right.MemberResolver.GenerateMemberId(rightMethod);
            rightSource = right.DecompilerService.DecompileEntitySnippet(rightMemberId);
        }

        var bodyDiff = leftSource != null && rightSource != null
            ? BuildBodyDiff(leftSource, rightSource, left.ContextAlias ?? "left", right.ContextAlias ?? "right")
            : null;

        return new MemberComparisonResult
        {
            LeftContextAlias = left.ContextAlias ?? "current",
            RightContextAlias = right.ContextAlias ?? "current",
            Symbol = symbol,
            SymbolKind = "Method",
            CompareMode = "body",
            LeftExists = leftMethod != null,
            RightExists = rightMethod != null,
            LeftSignature = leftMethod != null ? left.MemberResolver.GetMemberSignature(leftMethod) : null,
            RightSignature = rightMethod != null ? right.MemberResolver.GetMemberSignature(rightMethod) : null,
            SignatureChanged = leftMethod != null && rightMethod != null &&
                !string.Equals(left.MemberResolver.GetMemberSignature(leftMethod), right.MemberResolver.GetMemberSignature(rightMethod), StringComparison.Ordinal),
            BodyChanged = bodyDiff?.BodyChanged,
            BodyDiff = bodyDiff?.BodyDiff,
            DiffStats = bodyDiff?.DiffStats
        };
    }

    private static ITypeDefinition? FindType(ToolSessionView session, string typeName)
    {
        var allTypes = session.ContextManager.GetAllTypes();
        return allTypes.FirstOrDefault(type => string.Equals(type.FullName, typeName, StringComparison.Ordinal))
            ?? allTypes.FirstOrDefault(type => string.Equals(type.ReflectionName, typeName, StringComparison.Ordinal))
            ?? allTypes.FirstOrDefault(type => string.Equals(type.Name, typeName, StringComparison.Ordinal));
    }

    private static IMember? FindMember(ITypeDefinition type, string memberName, string expectedKind)
    {
        var matches = TypeSurfaceComparer.GetDirectMembers(type)
            .Where(member => string.Equals(member.Name, memberName, StringComparison.Ordinal))
            .Where(member => string.Equals(TypeSurfaceComparer.GetNormalizedMemberKind(member), expectedKind, StringComparison.Ordinal))
            .ToList();

        if (matches.Count == 0)
            return null;

        if (matches.Count > 1)
        {
            matches = matches
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .ThenBy(member => member.ToString(), StringComparer.Ordinal)
                .ToList();
        }

        return matches[0];
    }

    private static string NormalizeSymbolKind(string symbolKind, string symbol)
    {
        if (string.Equals(symbolKind, "auto", StringComparison.OrdinalIgnoreCase))
            return symbol.Contains(':', StringComparison.Ordinal) ? "method" : "type";

        return symbolKind.Trim().ToLowerInvariant();
    }

    private static string NormalizeCompareMode(string compareMode)
    {
        if (string.IsNullOrWhiteSpace(compareMode))
            return "surface";

        var normalized = compareMode.Trim().ToLowerInvariant();
        return normalized switch
        {
            "surface" => "surface",
            "body" => "body",
            "source" => "body",
            _ => throw new ArgumentException($"Unsupported compareMode '{compareMode}'. Supported values: 'surface'; for methods also 'body' (alias 'source').")
        };
    }

    private static MethodBodyDiffResult BuildBodyDiff(string leftSource, string rightSource, string leftLabel, string rightLabel)
    {
        var leftLines = PrepareDiffLines(leftSource);
        var rightLines = PrepareDiffLines(rightSource);
        var operations = BuildLineDiff(leftLines, rightLines);

        var addedLines = operations.Count(operation => operation.Kind == DiffLineKind.Inserted);
        var removedLines = operations.Count(operation => operation.Kind == DiffLineKind.Deleted);
        var changedBlocks = CountChangedBlocks(operations);
        var bodyChanged = addedLines > 0 || removedLines > 0;

        string? bodyDiff = null;
        if (bodyChanged)
        {
            var diffLines = new List<string>
            {
                $"--- {leftLabel}",
                $"+++ {rightLabel}",
                "@@"
            };

            foreach (var operation in operations)
            {
                diffLines.Add($"{GetDiffPrefix(operation.Kind)} {operation.Text}");
            }

            bodyDiff = string.Join('\n', diffLines);
        }

        return new MethodBodyDiffResult
        {
            BodyChanged = bodyChanged,
            BodyDiff = bodyDiff,
            DiffStats = new LineDiffStats
            {
                AddedLines = addedLines,
                RemovedLines = removedLines,
                ChangedBlocks = changedBlocks
            }
        };
    }

    private static string[] PrepareDiffLines(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.TrimEnd())
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0]))
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.ToArray();
    }

    private static List<DiffLine> BuildLineDiff(IReadOnlyList<string> leftLines, IReadOnlyList<string> rightLines)
    {
        var lcs = new int[leftLines.Count + 1, rightLines.Count + 1];
        for (var leftIndex = leftLines.Count - 1; leftIndex >= 0; leftIndex--)
        {
            for (var rightIndex = rightLines.Count - 1; rightIndex >= 0; rightIndex--)
            {
                lcs[leftIndex, rightIndex] = string.Equals(leftLines[leftIndex], rightLines[rightIndex], StringComparison.Ordinal)
                    ? lcs[leftIndex + 1, rightIndex + 1] + 1
                    : Math.Max(lcs[leftIndex + 1, rightIndex], lcs[leftIndex, rightIndex + 1]);
            }
        }

        var operations = new List<DiffLine>();
        var i = 0;
        var j = 0;

        while (i < leftLines.Count && j < rightLines.Count)
        {
            if (string.Equals(leftLines[i], rightLines[j], StringComparison.Ordinal))
            {
                operations.Add(new DiffLine(DiffLineKind.Equal, leftLines[i]));
                i++;
                j++;
            }
            else if (lcs[i + 1, j] >= lcs[i, j + 1])
            {
                operations.Add(new DiffLine(DiffLineKind.Deleted, leftLines[i]));
                i++;
            }
            else
            {
                operations.Add(new DiffLine(DiffLineKind.Inserted, rightLines[j]));
                j++;
            }
        }

        while (i < leftLines.Count)
        {
            operations.Add(new DiffLine(DiffLineKind.Deleted, leftLines[i]));
            i++;
        }

        while (j < rightLines.Count)
        {
            operations.Add(new DiffLine(DiffLineKind.Inserted, rightLines[j]));
            j++;
        }

        return operations;
    }

    private static int CountChangedBlocks(IReadOnlyList<DiffLine> operations)
    {
        var changedBlocks = 0;
        var insideChangedBlock = false;

        foreach (var operation in operations)
        {
            if (operation.Kind == DiffLineKind.Equal)
            {
                insideChangedBlock = false;
                continue;
            }

            if (!insideChangedBlock)
            {
                changedBlocks++;
                insideChangedBlock = true;
            }
        }

        return changedBlocks;
    }

    private static char GetDiffPrefix(DiffLineKind kind)
    {
        return kind switch
        {
            DiffLineKind.Equal => ' ',
            DiffLineKind.Deleted => '-',
            DiffLineKind.Inserted => '+',
            _ => ' '
        };
    }

    private static ParsedMemberSymbol ParseMemberSymbol(string symbol)
    {
        var parts = symbol.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
            return new ParsedMemberSymbol(parts[0], parts[1]);

        var lastDotIndex = symbol.LastIndexOf('.');
        if (lastDotIndex > 0 && lastDotIndex < symbol.Length - 1)
        {
            var typeName = symbol[..lastDotIndex].Trim();
            var memberName = symbol[(lastDotIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(typeName) && !string.IsNullOrWhiteSpace(memberName))
                return new ParsedMemberSymbol(typeName, memberName);
        }

        throw new ArgumentException($"Symbol '{symbol}' must use 'Namespace.Type:MemberName' or 'Namespace.Type.MemberName'.");
    }
}

internal sealed record ParsedMemberSymbol(string TypeName, string MemberName);
internal sealed record DiffLine(DiffLineKind Kind, string Text);

internal enum DiffLineKind
{
    Equal,
    Deleted,
    Inserted
}

public sealed record ComparedMemberSummary
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string Signature { get; init; }
}

public sealed record ChangedMemberSummary
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string LeftSignature { get; init; }
    public required string RightSignature { get; init; }
}

public sealed record TypeComparisonResult
{
    public required string LeftContextAlias { get; init; }
    public required string RightContextAlias { get; init; }
    public required string Symbol { get; init; }
    public required string SymbolKind { get; init; }
    public required string CompareMode { get; init; }
    public bool LeftExists { get; init; }
    public bool RightExists { get; init; }
    public required List<ComparedMemberSummary> AddedMembers { get; init; }
    public required List<ComparedMemberSummary> RemovedMembers { get; init; }
    public required List<ChangedMemberSummary> ChangedMembers { get; init; }
}

public sealed record MemberComparisonResult
{
    public required string LeftContextAlias { get; init; }
    public required string RightContextAlias { get; init; }
    public required string Symbol { get; init; }
    public required string SymbolKind { get; init; }
    public required string CompareMode { get; init; }
    public bool LeftExists { get; init; }
    public bool RightExists { get; init; }
    public string? LeftSignature { get; init; }
    public string? RightSignature { get; init; }
    public bool SignatureChanged { get; init; }
    public bool? BodyChanged { get; init; }
    public string? BodyDiff { get; init; }
    public LineDiffStats? DiffStats { get; init; }
}

public sealed record MethodBodyDiffResult
{
    public bool BodyChanged { get; init; }
    public string? BodyDiff { get; init; }
    public required LineDiffStats DiffStats { get; init; }
}

public sealed record LineDiffStats
{
    public int AddedLines { get; init; }
    public int RemovedLines { get; init; }
    public int ChangedBlocks { get; init; }
}

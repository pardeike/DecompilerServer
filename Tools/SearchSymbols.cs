using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

[McpServerToolType]
public static class SearchSymbolsTool
{
    [McpServerTool, Description("Search types and members together from fragments. For fully-qualified stale guesses, prefer resolve_member_id; this tool still returns a type/member fallback when Type.MissingMember resolves the type.")]
    public static string SearchSymbols(
        string query,
        string? kind = null,
        string? namespaceFilter = null,
        string? declaringTypeFilter = null,
        bool includeTypes = true,
        bool includeMembers = true,
        bool includeCompilerGenerated = false,
        int limit = 50,
        string? cursor = null,
        string mode = "discovery",
        string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute<object>(() =>
        {
            var session = ToolSessionRouter.GetForContext(contextAlias);
            var contextManager = session.ContextManager;
            var memberResolver = session.MemberResolver;

            if (!contextManager.IsLoaded)
                throw new InvalidOperationException("No assembly loaded");

            var normalizedLimit = MemberSummaryModes.ClampLimit(limit, 50);
            var parsedMode = MemberSummaryModes.Parse(mode, MemberSummaryMode.Discovery);
            var normalizedKind = NormalizeKind(kind);
            var items = new List<MemberSummary>();
            object? diagnostic = null;

            if (includeTypes && (normalizedKind == null || normalizedKind == "type"))
            {
                IEnumerable<ITypeDefinition> types = FilterCompilerGeneratedTypes(contextManager.GetAllTypes(), includeCompilerGenerated);
                if (!string.IsNullOrWhiteSpace(namespaceFilter))
                    types = types.Where(type => type.Namespace.StartsWith(namespaceFilter, StringComparison.OrdinalIgnoreCase));

                items.AddRange(types
                    .Where(type => SymbolResolutionDiagnostics.MatchesType(type, query))
                    .Select(type => SymbolResolutionDiagnostics.CreateTypeSummary(type, memberResolver)));
            }

            if (includeMembers && normalizedKind != "type")
            {
                IEnumerable<ITypeDefinition> types = FilterCompilerGeneratedTypes(contextManager.GetAllTypes(), includeCompilerGenerated);
                if (!string.IsNullOrWhiteSpace(namespaceFilter))
                    types = types.Where(type => type.Namespace.StartsWith(namespaceFilter, StringComparison.OrdinalIgnoreCase));

                foreach (var type in types)
                {
                    foreach (var member in SymbolResolutionDiagnostics.GetDirectMembers(type))
                    {
                        if (!includeCompilerGenerated && TypeSurfaceComparer.IsCompilerGenerated(member))
                            continue;

                        if (!MatchesKind(member, normalizedKind))
                            continue;

                        if (!string.IsNullOrWhiteSpace(declaringTypeFilter) && !MatchesDeclaringType(member, declaringTypeFilter))
                            continue;

                        if (!SymbolResolutionDiagnostics.MatchesMember(member, query, memberResolver))
                            continue;

                        items.Add(SymbolResolutionDiagnostics.CreateMemberSummary(member, memberResolver));
                    }
                }
            }

            var ordered = items
                .GroupBy(item => item.MemberId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => Rank(item, query))
                .ThenBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ordered.Count == 0)
            {
                var fallback = TryBuildResolvedTypeFallback(
                    query,
                    contextManager,
                    memberResolver,
                    normalizedKind,
                    includeTypes,
                    includeMembers,
                    includeCompilerGenerated);

                if (fallback != null)
                {
                    ordered = fallback.Items
                        .GroupBy(item => item.MemberId, StringComparer.Ordinal)
                        .Select(group => group.First())
                        .OrderBy(item => Rank(item, query))
                        .ThenBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    diagnostic = fallback.Diagnostic;
                }
            }

            var startIndex = ParseCursor(cursor);

            var pageItems = ordered.Skip(startIndex).Take(normalizedLimit).ToList();
            var hasMore = startIndex + normalizedLimit < ordered.Count;
            var result = new SearchResult<MemberSummary>(
                pageItems,
                hasMore,
                hasMore ? (startIndex + normalizedLimit).ToString() : null,
                ordered.Count);

            var projected = MemberSummaryModes.Project(result, parsedMode);
            if (diagnostic == null)
                return projected;

            return new
            {
                projected.Items,
                projected.HasMore,
                projected.NextCursor,
                projected.TotalEstimate,
                diagnostic
            };
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

    private static string? NormalizeKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return null;

        return kind.Trim().ToLowerInvariant() switch
        {
            "ctor" or "constructor" => "method",
            "method" => "method",
            "field" => "field",
            "property" => "property",
            "event" => "event",
            "type" => "type",
            _ => kind.Trim().ToLowerInvariant()
        };
    }

    private static bool MatchesKind(IMember member, string? normalizedKind)
    {
        if (normalizedKind == null)
            return true;

        return normalizedKind switch
        {
            "method" => member is IMethod,
            "field" => member is IField,
            "property" => member is IProperty,
            "event" => member is IEvent,
            _ => true
        };
    }

    private static bool MatchesDeclaringType(IMember member, string declaringTypeFilter)
    {
        var declaringType = member.DeclaringType;
        if (declaringType == null)
            return false;

        return string.Equals(declaringType.Name, declaringTypeFilter, StringComparison.OrdinalIgnoreCase)
            || string.Equals(declaringType.FullName, declaringTypeFilter, StringComparison.OrdinalIgnoreCase)
            || string.Equals(declaringType.ReflectionName, declaringTypeFilter, StringComparison.OrdinalIgnoreCase)
            || declaringType.FullName.EndsWith("." + declaringTypeFilter, StringComparison.OrdinalIgnoreCase)
            || declaringType.ReflectionName.EndsWith("+" + declaringTypeFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static int Rank(MemberSummary summary, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return 100;

        var penalty = IsLikelyCompilerGenerated(summary) ? 100 : 0;

        if (string.Equals(summary.FullName, query, StringComparison.OrdinalIgnoreCase))
            return penalty;

        if (string.Equals(summary.Name, query, StringComparison.OrdinalIgnoreCase))
            return penalty + 1;

        if (summary.FullName.EndsWith("." + query, StringComparison.OrdinalIgnoreCase)
            || summary.FullName.EndsWith(":" + query, StringComparison.OrdinalIgnoreCase))
            return penalty + 2;

        if (!string.IsNullOrWhiteSpace(summary.DeclaringType)
            && (string.Equals(summary.DeclaringType, query, StringComparison.OrdinalIgnoreCase)
                || summary.DeclaringType.EndsWith("." + query, StringComparison.OrdinalIgnoreCase)
                || summary.DeclaringType.EndsWith("+" + query, StringComparison.OrdinalIgnoreCase)))
            return penalty + 3;

        return penalty + 10;
    }

    private static IEnumerable<ITypeDefinition> FilterCompilerGeneratedTypes(IEnumerable<ITypeDefinition> types, bool includeCompilerGenerated)
    {
        return includeCompilerGenerated
            ? types
            : types.Where(type => !TypeSurfaceComparer.IsCompilerGenerated(type));
    }

    private static SearchSymbolsFallback? TryBuildResolvedTypeFallback(
        string query,
        AssemblyContextManager contextManager,
        MemberResolver memberResolver,
        string? normalizedKind,
        bool includeTypes,
        bool includeMembers,
        bool includeCompilerGenerated)
    {
        var guess = ResolveTypeMemberGuess(query, contextManager);
        if (guess == null)
            return null;

        var (type, missingMember) = guess.Value;
        if (!includeCompilerGenerated && TypeSurfaceComparer.IsCompilerGenerated(type))
            return null;

        var typeSummary = SymbolResolutionDiagnostics.CreateTypeSummary(type, memberResolver);
        var items = new List<MemberSummary>();
        if (includeTypes && (normalizedKind == null || normalizedKind == "type"))
            items.Add(typeSummary);

        if (includeMembers && normalizedKind != "type")
        {
            items.AddRange(SymbolResolutionDiagnostics.GetDirectMembers(type)
                .Where(member => includeCompilerGenerated || !TypeSurfaceComparer.IsCompilerGenerated(member))
                .Where(member => MatchesKind(member, normalizedKind))
                .OrderBy(member => RankFallbackMember(member, missingMember))
                .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                .Select(member => SymbolResolutionDiagnostics.CreateMemberSummary(member, memberResolver)));
        }

        if (items.Count == 0)
            return null;

        var hints = new[]
        {
            new ToolErrorHint(
                "list_members",
                new { typeId = typeSummary.MemberId, mode = "signatures" },
                "The type resolved; inspect direct members before guessing another method name."),
            new ToolErrorHint(
                "resolve_member_id",
                new { memberId = query },
                "Use resolve_member_id for fully-qualified guesses when you want structured candidates and hints.")
        };

        var diagnostic = new
        {
            code = "member_guess_unresolved",
            message = $"Type '{type.FullName}' was found, but member '{missingMember}' did not match a direct symbol. Returned the type and direct members instead.",
            query,
            missingMember,
            resolvedType = typeSummary,
            hints
        };

        return new SearchSymbolsFallback(items, diagnostic);
    }

    private static (ITypeDefinition Type, string MemberName)? ResolveTypeMemberGuess(string query, AssemblyContextManager contextManager)
    {
        var value = NormalizeSymbolGuess(query);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var colonIndex = value.LastIndexOf(':');
        if (colonIndex > 0 && colonIndex < value.Length - 1)
        {
            var typeName = value[..colonIndex];
            var memberName = value[(colonIndex + 1)..];
            var type = SymbolResolutionDiagnostics.FindBestType(typeName, contextManager);
            if (type != null)
                return (type, memberName);
        }

        for (var index = value.LastIndexOf('.'); index > 0; index = value.LastIndexOf('.', index - 1))
        {
            if (index >= value.Length - 1)
                continue;

            var typeName = value[..index];
            var memberName = value[(index + 1)..];
            var type = SymbolResolutionDiagnostics.FindBestType(typeName, contextManager);
            if (type != null)
                return (type, memberName);
        }

        return null;
    }

    private static string NormalizeSymbolGuess(string query)
    {
        var value = query.Trim();
        if (value.Length > 2 && value[1] == ':' && "TMFPE".Contains(value[0]))
            value = value[2..];

        var parenIndex = value.IndexOf('(');
        if (parenIndex >= 0)
            value = value[..parenIndex];

        return value;
    }

    private static int RankFallbackMember(IMember member, string missingMember)
    {
        if (string.Equals(member.Name, missingMember, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (member.Name.Contains(missingMember, StringComparison.OrdinalIgnoreCase)
            || missingMember.Contains(member.Name, StringComparison.OrdinalIgnoreCase))
            return 1;

        if (member.IsOverride)
            return 2;

        if (member.IsVirtual || member.IsAbstract)
            return 3;

        return member is IMethod method && method.IsConstructor ? 20 : 10;
    }

    private static bool IsLikelyCompilerGenerated(MemberSummary summary)
    {
        return StartsGenerated(summary.Name)
            || StartsGenerated(summary.FullName)
            || StartsGenerated(summary.DeclaringType)
            || ContainsGeneratedSegment(summary.FullName)
            || ContainsGeneratedSegment(summary.DeclaringType);
    }

    private static bool StartsGenerated(string? value)
    {
        return value?.StartsWith("<", StringComparison.Ordinal) == true;
    }

    private static bool ContainsGeneratedSegment(string? value)
    {
        return value?.Contains(".<", StringComparison.Ordinal) == true
            || value?.Contains("+<", StringComparison.Ordinal) == true;
    }

    private sealed record SearchSymbolsFallback(List<MemberSummary> Items, object Diagnostic);
}

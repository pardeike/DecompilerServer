using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class SearchSymbolsTool
{
    [McpServerTool, Description("Search types and members together from partial or qualified names. Use first when exploring an unknown assembly or recovering from a bad member guess.")]
    public static string SearchSymbols(
        string query,
        string? kind = null,
        string? namespaceFilter = null,
        string? declaringTypeFilter = null,
        bool includeTypes = true,
        bool includeMembers = true,
        int limit = 50,
        string? cursor = null,
        string mode = "discovery",
        string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
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

            if (includeTypes && (normalizedKind == null || normalizedKind == "type"))
            {
                IEnumerable<ICSharpCode.Decompiler.TypeSystem.ITypeDefinition> types = contextManager.GetAllTypes();
                if (!string.IsNullOrWhiteSpace(namespaceFilter))
                    types = types.Where(type => type.Namespace.StartsWith(namespaceFilter, StringComparison.OrdinalIgnoreCase));

                items.AddRange(types
                    .Where(type => SymbolResolutionDiagnostics.MatchesType(type, query))
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
                    }));
            }

            if (includeMembers && normalizedKind != "type")
            {
                IEnumerable<ICSharpCode.Decompiler.TypeSystem.ITypeDefinition> types = contextManager.GetAllTypes();
                if (!string.IsNullOrWhiteSpace(namespaceFilter))
                    types = types.Where(type => type.Namespace.StartsWith(namespaceFilter, StringComparison.OrdinalIgnoreCase));

                foreach (var type in types)
                {
                    var members = type.Methods.Cast<ICSharpCode.Decompiler.TypeSystem.IMember>()
                        .Concat(type.Fields)
                        .Concat(type.Properties)
                        .Concat(type.Events);

                    foreach (var member in members)
                    {
                        if (!MatchesKind(member, normalizedKind))
                            continue;

                        if (!string.IsNullOrWhiteSpace(declaringTypeFilter) && !MatchesDeclaringType(member, declaringTypeFilter))
                            continue;

                        if (!SymbolResolutionDiagnostics.MatchesMember(member, query, memberResolver))
                            continue;

                        items.Add(new MemberSummary
                        {
                            MemberId = memberResolver.GenerateMemberId(member),
                            Name = member.Name,
                            FullName = member.FullName,
                            Kind = GetMemberKind(member),
                            DeclaringType = member.DeclaringType?.FullName,
                            Namespace = member.DeclaringType?.Namespace,
                            Signature = memberResolver.GetMemberSignature(member),
                            Accessibility = member.Accessibility.ToString(),
                            IsStatic = member.IsStatic,
                            IsAbstract = member.IsAbstract,
                            IsVirtual = member.IsVirtual
                        });
                    }
                }
            }

            var ordered = items
                .GroupBy(item => item.MemberId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(item => Rank(item, query))
                .ThenBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var startIndex = 0;
            if (!string.IsNullOrWhiteSpace(cursor) && int.TryParse(cursor, out var cursorIndex))
                startIndex = Math.Max(0, cursorIndex);

            var pageItems = ordered.Skip(startIndex).Take(normalizedLimit).ToList();
            var hasMore = startIndex + normalizedLimit < ordered.Count;
            var result = new SearchResult<MemberSummary>(
                pageItems,
                hasMore,
                hasMore ? (startIndex + normalizedLimit).ToString() : null,
                ordered.Count);

            return MemberSummaryModes.Project(result, parsedMode);
        });
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

    private static bool MatchesKind(ICSharpCode.Decompiler.TypeSystem.IMember member, string? normalizedKind)
    {
        if (normalizedKind == null)
            return true;

        return normalizedKind switch
        {
            "method" => member is ICSharpCode.Decompiler.TypeSystem.IMethod,
            "field" => member is ICSharpCode.Decompiler.TypeSystem.IField,
            "property" => member is ICSharpCode.Decompiler.TypeSystem.IProperty,
            "event" => member is ICSharpCode.Decompiler.TypeSystem.IEvent,
            _ => true
        };
    }

    private static bool MatchesDeclaringType(ICSharpCode.Decompiler.TypeSystem.IMember member, string declaringTypeFilter)
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

        if (string.Equals(summary.FullName, query, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (string.Equals(summary.Name, query, StringComparison.OrdinalIgnoreCase))
            return 1;

        return 10;
    }

    private static string GetMemberKind(ICSharpCode.Decompiler.TypeSystem.IMember member)
    {
        return member switch
        {
            ICSharpCode.Decompiler.TypeSystem.IMethod method when method.IsConstructor => "Constructor",
            ICSharpCode.Decompiler.TypeSystem.IMethod => "Method",
            ICSharpCode.Decompiler.TypeSystem.IField => "Field",
            ICSharpCode.Decompiler.TypeSystem.IProperty => "Property",
            ICSharpCode.Decompiler.TypeSystem.IEvent => "Event",
            _ => "Unknown"
        };
    }
}

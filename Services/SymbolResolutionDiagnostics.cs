using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer.Services;

public static class SymbolResolutionDiagnostics
{
    public static ToolErrorException CreateUnresolvedMemberError(
        string input,
        AssemblyContextManager contextManager,
        MemberResolver memberResolver,
        string? expectedKind = null)
    {
        if (!contextManager.IsLoaded)
            return new ToolErrorException("no_assembly_loaded", "No assembly loaded");

        var symbol = SymbolParts.Parse(input);
        var hints = new List<ToolErrorHint>();
        var candidates = new List<MemberSummary>();
        MemberSummary? resolvedType = null;
        string code = "invalid_member_id";
        string message = $"Member ID '{input}' could not be resolved";

        if (!string.IsNullOrWhiteSpace(symbol.TypeName))
        {
            var type = FindBestType(symbol.TypeName!, contextManager);
            if (type != null)
            {
                resolvedType = CreateTypeSummary(type, memberResolver);
                hints.Add(new ToolErrorHint(
                    "get_members_of_type",
                    new { typeId = resolvedType.MemberId, mode = "signatures" },
                    "The type resolved; inspect its direct members."));

                if (!string.IsNullOrWhiteSpace(symbol.MemberName))
                {
                    candidates.AddRange(FindNearbyMembers(type, symbol.MemberName!, memberResolver, expectedKind));
                    code = "member_not_found";
                    message = $"Type '{type.FullName}' was found, but member '{symbol.MemberName}' was not.";
                }
                else if (!string.Equals(expectedKind, "type", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.AddRange(FindInterestingMembers(type, memberResolver, expectedKind));
                    code = "member_not_found";
                    message = $"Type '{type.FullName}' was found, but no member name was supplied.";
                }
            }
            else
            {
                code = "type_not_found";
                message = $"Type '{symbol.TypeName}' could not be found.";
            }
        }

        if (candidates.Count == 0)
        {
            candidates.AddRange(SearchSymbols(input, contextManager, memberResolver, expectedKind, limit: 10));
        }

        var query = !string.IsNullOrWhiteSpace(symbol.MemberName)
            ? symbol.MemberName!
            : !string.IsNullOrWhiteSpace(symbol.TypeName)
                ? symbol.TypeName!
                : input;

        hints.Add(new ToolErrorHint(
            "search_symbols",
            new { query, kind = expectedKind, limit = 10 },
            "Search canonical symbols before calling memberId follow-up tools."));

        if (!string.IsNullOrWhiteSpace(symbol.TypeName))
        {
            hints.Add(new ToolErrorHint(
                "search_types",
                new { query = symbol.TypeName, limit = 10 },
                "Find the canonical type memberId."));
        }

        var details = new
        {
            input,
            expectedKind,
            parsed = new
            {
                symbol.Prefix,
                symbol.TypeName,
                symbol.MemberName
            },
            resolvedType,
            candidates = candidates
                .GroupBy(candidate => candidate.MemberId, StringComparer.Ordinal)
                .Select(group => group.First())
                .Take(10)
                .ToArray()
        };

        return new ToolErrorException(code, message, details, hints);
    }

    public static ToolErrorException CreateWrongKindError(
        string input,
        string expectedKind,
        IEntity actual,
        MemberResolver memberResolver)
    {
        return new ToolErrorException(
            "wrong_symbol_kind",
            $"Symbol '{input}' resolved, but it is not a {expectedKind}.",
            new
            {
                input,
                expectedKind,
                actual = CreateSummary(actual, memberResolver)
            });
    }

    public static IReadOnlyList<MemberSummary> SearchSymbols(
        string query,
        AssemblyContextManager contextManager,
        MemberResolver memberResolver,
        string? kind = null,
        int limit = 50)
    {
        if (!contextManager.IsLoaded)
            return Array.Empty<MemberSummary>();

        var normalizedKind = NormalizeKind(kind);
        var allTypes = contextManager.GetAllTypes();
        var results = new List<MemberSummary>();

        if (normalizedKind == null || normalizedKind == "type")
        {
            results.AddRange(allTypes
                .Where(type => MatchesType(type, query))
                .Select(type => CreateTypeSummary(type, memberResolver)));
        }

        if (normalizedKind != "type")
        {
            foreach (var type in allTypes)
            {
                foreach (var member in GetDirectMembers(type))
                {
                    if (!MatchesKind(member, normalizedKind))
                        continue;

                    if (MatchesMember(member, query, memberResolver))
                        results.Add(CreateMemberSummary(member, memberResolver));
                }
            }
        }

        return results
            .OrderBy(summary => Rank(summary, query))
            .ThenBy(summary => summary.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, limit))
            .ToArray();
    }

    public static bool MatchesType(ITypeDefinition type, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return Contains(type.Name, query)
            || Contains(type.FullName, query)
            || Contains(type.ReflectionName, query);
    }

    public static bool MatchesMember(IMember member, string query, MemberResolver memberResolver)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var declaringType = member.DeclaringType;
        var signature = memberResolver.GetMemberSignature(member);

        return Contains(member.Name, query)
            || Contains(member.FullName, query)
            || Contains(signature, query)
            || (declaringType != null && Contains($"{declaringType.FullName}.{member.Name}", query))
            || (declaringType != null && Contains($"{declaringType.FullName}:{member.Name}", query));
    }

    private static ITypeDefinition? FindBestType(string typeName, AssemblyContextManager contextManager)
    {
        var direct = contextManager.FindTypeByName(typeName);
        if (direct != null)
            return direct;

        var types = contextManager.GetAllTypes().ToArray();

        return types.FirstOrDefault(type => string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase))
            ?? types.FirstOrDefault(type => string.Equals(type.ReflectionName, typeName, StringComparison.OrdinalIgnoreCase))
            ?? types.FirstOrDefault(type => string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase))
            ?? types.FirstOrDefault(type => type.FullName.EndsWith("." + typeName, StringComparison.OrdinalIgnoreCase))
            ?? types.FirstOrDefault(type => type.ReflectionName.EndsWith("+" + typeName, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<MemberSummary> FindNearbyMembers(
        ITypeDefinition type,
        string memberName,
        MemberResolver memberResolver,
        string? expectedKind)
    {
        var normalizedKind = NormalizeKind(expectedKind);
        var direct = GetDirectMembers(type)
            .Where(member => MatchesKind(member, normalizedKind))
            .ToArray();

        var directMatches = direct
            .Where(member => Contains(member.Name, memberName) || Contains(memberName, member.Name) || ShareMeaningfulToken(member.Name, memberName))
            .Select(member => CreateMemberSummary(member, memberResolver));

        var likelyOverrides = direct
            .Where(member => member.IsOverride || member.IsVirtual || member.IsAbstract)
            .Select(member => CreateMemberSummary(member, memberResolver));

        return directMatches
            .Concat(likelyOverrides)
            .Concat(direct.Select(member => CreateMemberSummary(member, memberResolver)))
            .GroupBy(summary => summary.MemberId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(10);
    }

    private static IEnumerable<MemberSummary> FindInterestingMembers(
        ITypeDefinition type,
        MemberResolver memberResolver,
        string? expectedKind)
    {
        var normalizedKind = NormalizeKind(expectedKind);
        return GetDirectMembers(type)
            .Where(member => MatchesKind(member, normalizedKind))
            .OrderByDescending(member => member.IsOverride)
            .ThenByDescending(member => member.IsVirtual)
            .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .Select(member => CreateMemberSummary(member, memberResolver));
    }

    private static IEnumerable<IMember> GetDirectMembers(ITypeDefinition type)
    {
        return type.Methods.Cast<IMember>()
            .Concat(type.Fields)
            .Concat(type.Properties)
            .Concat(type.Events);
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

    private static int Rank(MemberSummary summary, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return 100;

        if (string.Equals(summary.FullName, query, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (string.Equals(summary.Name, query, StringComparison.OrdinalIgnoreCase))
            return 1;

        if (summary.FullName.EndsWith("." + query, StringComparison.OrdinalIgnoreCase)
            || summary.FullName.EndsWith(":" + query, StringComparison.OrdinalIgnoreCase))
            return 2;

        return 10;
    }

    private static bool Contains(string? text, string query)
    {
        return text != null && text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShareMeaningfulToken(string left, string right)
    {
        var leftTokens = SplitIdentifier(left).Where(token => token.Length >= 4).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (leftTokens.Count == 0)
            return false;

        return SplitIdentifier(right).Any(token => token.Length >= 4 && leftTokens.Contains(token));
    }

    private static IEnumerable<string> SplitIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var current = new List<char>();
        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                foreach (var token in Flush())
                    yield return token;
                continue;
            }

            if (current.Count > 0 && char.IsUpper(ch) && !char.IsUpper(current[^1]))
            {
                foreach (var token in Flush())
                    yield return token;
            }

            current.Add(ch);
        }

        foreach (var token in Flush())
            yield return token;

        IEnumerable<string> Flush()
        {
            if (current.Count == 0)
                yield break;

            var token = new string(current.ToArray());
            current.Clear();
            yield return token;
        }
    }

    private static MemberSummary CreateSummary(IEntity entity, MemberResolver memberResolver)
    {
        return entity switch
        {
            ITypeDefinition type => CreateTypeSummary(type, memberResolver),
            IMember member => CreateMemberSummary(member, memberResolver),
            _ => new MemberSummary
            {
                MemberId = memberResolver.GenerateMemberId(entity),
                Name = entity.Name,
                FullName = entity.FullName,
                Kind = entity.GetType().Name,
                DeclaringType = null,
                Namespace = null,
                Signature = memberResolver.GetMemberSignature(entity),
                Accessibility = "Unknown",
                IsStatic = false,
                IsAbstract = false
            }
        };
    }

    private static MemberSummary CreateTypeSummary(ITypeDefinition type, MemberResolver memberResolver)
    {
        return new MemberSummary
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
        };
    }

    private static MemberSummary CreateMemberSummary(IMember member, MemberResolver memberResolver)
    {
        return new MemberSummary
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
        };
    }

    private static string GetMemberKind(IMember member)
    {
        return member switch
        {
            IMethod method when method.IsConstructor => "Constructor",
            IMethod => "Method",
            IField => "Field",
            IProperty => "Property",
            IEvent => "Event",
            _ => "Unknown"
        };
    }

    private sealed record SymbolParts(string? Prefix, string? TypeName, string? MemberName)
    {
        public static SymbolParts Parse(string input)
        {
            var trimmed = input.Trim();
            string? prefix = null;
            var body = trimmed;

            if (trimmed.Length > 2 && trimmed[1] == ':' && "TMFPE".Contains(trimmed[0]))
            {
                prefix = trimmed[..1];
                body = trimmed[2..];
            }

            body = StripParameterList(body);

            if (prefix == "T")
                return new SymbolParts(prefix, body, null);

            var colonIndex = body.LastIndexOf(':');
            if (colonIndex > 0 && colonIndex < body.Length - 1)
                return new SymbolParts(prefix, body[..colonIndex], body[(colonIndex + 1)..]);

            var lastDot = body.LastIndexOf('.');
            if (lastDot > 0 && lastDot < body.Length - 1)
                return new SymbolParts(prefix, body[..lastDot], body[(lastDot + 1)..]);

            return new SymbolParts(prefix, body, null);
        }

        private static string StripParameterList(string value)
        {
            var paren = value.IndexOf('(');
            return paren >= 0 ? value[..paren] : value;
        }
    }
}

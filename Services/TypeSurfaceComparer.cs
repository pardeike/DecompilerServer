using ICSharpCode.Decompiler.TypeSystem;
using DecompilerServer.Services;

namespace DecompilerServer;

internal static class TypeSurfaceComparer
{
    public static TypeSurfaceDiff Compare(
        ITypeDefinition? leftType,
        ITypeDefinition? rightType,
        MemberResolver leftResolver,
        MemberResolver rightResolver)
    {
        var result = new TypeSurfaceDiff
        {
            LeftExists = leftType != null,
            RightExists = rightType != null,
            AddedMembers = new List<ComparedMemberSummary>(),
            RemovedMembers = new List<ComparedMemberSummary>(),
            ChangedMembers = new List<ChangedMemberSummary>()
        };

        if (leftType == null || rightType == null)
            return result;

        var leftMembers = BuildTypeMemberMap(leftType, leftResolver);
        var rightMembers = BuildTypeMemberMap(rightType, rightResolver);

        foreach (var key in rightMembers.Keys.Except(leftMembers.Keys).OrderBy(key => key, StringComparer.Ordinal))
        {
            result.AddedMembers.Add(rightMembers[key]);
        }

        foreach (var key in leftMembers.Keys.Except(rightMembers.Keys).OrderBy(key => key, StringComparer.Ordinal))
        {
            result.RemovedMembers.Add(leftMembers[key]);
        }

        foreach (var key in leftMembers.Keys.Intersect(rightMembers.Keys).OrderBy(key => key, StringComparer.Ordinal))
        {
            var leftMember = leftMembers[key];
            var rightMember = rightMembers[key];
            if (!string.Equals(leftMember.Signature, rightMember.Signature, StringComparison.Ordinal))
            {
                result.ChangedMembers.Add(new ChangedMemberSummary
                {
                    Name = leftMember.Name,
                    Kind = leftMember.Kind,
                    LeftSignature = leftMember.Signature,
                    RightSignature = rightMember.Signature
                });
            }
        }

        return result;
    }

    public static IEnumerable<IMember> GetDirectMembers(ITypeDefinition type)
    {
        return type.Methods.Cast<IMember>()
            .Concat(type.Fields)
            .Concat(type.Properties)
            .Concat(type.Events);
    }

    public static string GetNormalizedMemberKind(IMember member)
    {
        return member switch
        {
            IMethod => "method",
            IField => "field",
            IProperty => "property",
            IEvent => "event",
            _ => "unknown"
        };
    }

    public static string ToDisplayKind(string normalizedKind)
    {
        return normalizedKind switch
        {
            "type" => "Type",
            "method" => "Method",
            "field" => "Field",
            "property" => "Property",
            "event" => "Event",
            _ => normalizedKind
        };
    }

    public static bool IsCompilerGenerated(ITypeDefinition type)
    {
        if (type.Name.StartsWith("<", StringComparison.Ordinal))
            return true;

        return type.GetAttributes().Any(attribute =>
            string.Equals(
                attribute.AttributeType.FullName,
                "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
                StringComparison.Ordinal));
    }

    private static Dictionary<string, ComparedMemberSummary> BuildTypeMemberMap(ITypeDefinition type, MemberResolver memberResolver)
    {
        return GetDirectMembers(type)
            .GroupBy(member => $"{GetNormalizedMemberKind(member)}:{member.Name}", StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var ordered = group
                        .Select(member => memberResolver.GetMemberSignature(member))
                        .OrderBy(signature => signature, StringComparer.Ordinal)
                        .ToArray();

                    return new ComparedMemberSummary
                    {
                        Name = group.First().Name,
                        Kind = ToDisplayKind(GetNormalizedMemberKind(group.First())),
                        Signature = string.Join(" | ", ordered)
                    };
                },
                StringComparer.Ordinal);
    }
}

internal sealed record TypeSurfaceDiff
{
    public bool LeftExists { get; init; }
    public bool RightExists { get; init; }
    public required List<ComparedMemberSummary> AddedMembers { get; init; }
    public required List<ComparedMemberSummary> RemovedMembers { get; init; }
    public required List<ChangedMemberSummary> ChangedMembers { get; init; }
}

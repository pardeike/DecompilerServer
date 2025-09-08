using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

[McpServerToolType]
public static class GetMembersOfTypeTool
{
    [McpServerTool, Description("List members of a given type with filters and pagination.")]
    public static string GetMembersOfType(string typeId, string? kind = null, string? accessibility = null, bool? isStatic = null, bool includeInherited = false, int limit = 100, string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Resolve the type
            var entity = memberResolver.ResolveMember(typeId);
            if (entity is not ITypeDefinition type)
            {
                throw new ArgumentException($"Type ID '{typeId}' could not be resolved to a type");
            }

            // Get members
            IEnumerable<IMember> members;
            if (includeInherited)
            {
                // Include inherited members from base classes and interfaces
                members = GetAllMembersIncludingInherited(type);
            }
            else
            {
                // Only direct members
                members = GetDirectMembers(type);
            }

            // Apply filters
            if (!string.IsNullOrEmpty(kind))
            {
                members = members.Where(m => MatchesKind(m, kind));
            }

            if (!string.IsNullOrEmpty(accessibility))
            {
                members = members.Where(m => string.Equals(m.Accessibility.ToString(), accessibility, StringComparison.OrdinalIgnoreCase));
            }

            if (isStatic.HasValue)
            {
                members = members.Where(m => m.IsStatic == isStatic.Value);
            }

            // Sort for consistent ordering
            var sortedMembers = members.OrderBy(m => m.Name).ThenBy(m => m.SymbolKind).ToList();

            // Apply pagination
            var startIndex = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
            {
                startIndex = cursorIndex;
            }

            var pageItems = sortedMembers
                .Skip(startIndex)
                .Take(limit)
                .Select(member => CreateMemberSummary(member, memberResolver))
                .ToList();

            var hasMore = startIndex + limit < sortedMembers.Count;
            var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

            var result = new SearchResult<MemberSummary>(pageItems, hasMore, nextCursor, sortedMembers.Count);

            return result;
        });
    }

    private static IEnumerable<IMember> GetDirectMembers(ITypeDefinition type)
    {
        return type.Methods.Cast<IMember>()
            .Concat(type.Fields)
            .Concat(type.Properties)
            .Concat(type.Events);
    }

    private static IEnumerable<IMember> GetAllMembersIncludingInherited(ITypeDefinition type)
    {
        var members = new List<IMember>();

        // Add direct members
        members.AddRange(GetDirectMembers(type));

        // Add inherited members from base types
        var baseType = type.DirectBaseTypes.FirstOrDefault(bt => bt.Kind == TypeKind.Class);
        if (baseType?.GetDefinition() is ITypeDefinition baseDefinition &&
            baseDefinition.FullName != "System.Object")
        {
            members.AddRange(GetAllMembersIncludingInherited(baseDefinition));
        }

        // Add interface members
        foreach (var interfaceType in type.DirectBaseTypes.Where(bt => bt.Kind == TypeKind.Interface))
        {
            if (interfaceType.GetDefinition() is ITypeDefinition interfaceDefinition)
            {
                members.AddRange(GetDirectMembers(interfaceDefinition));
            }
        }

        return members.Distinct(); // Remove duplicates from overrides
    }

    private static bool MatchesKind(IMember member, string kind)
    {
        var memberKind = member switch
        {
            IMethod method when method.IsConstructor => "constructor",
            IMethod => "method",
            IField => "field",
            IProperty => "property",
            IEvent => "event",
            _ => "unknown"
        };

        return string.Equals(memberKind, kind, StringComparison.OrdinalIgnoreCase);
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
}

using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;
using System.Text.RegularExpressions;

namespace DecompilerServer;

public static class NormalizeMemberIdTool
{
    [McpServerTool, Description("Normalize a possibly partial or human-entered identifier into a canonical memberId.")]
    public static string NormalizeMemberId(string input)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // First, try to resolve as-is (it might already be a valid member ID)
            try
            {
                var directResolve = memberResolver.ResolveMember(input);
                if (directResolve != null)
                {
                    var normalizedId = memberResolver.GenerateMemberId(directResolve);
                    return new
                    {
                        normalizedId = (string?)normalizedId,
                        candidates = (MemberSummary[]?)null
                    };
                }
            }
            catch
            {
                // Continue with normalization attempts
            }

            // Try different normalization approaches
            var candidates = new List<MemberSummary>();

            // Pattern: "Type:Member" or "Namespace.Type:Member"
            if (input.Contains(':'))
            {
                var parts = input.Split(':', 2);
                if (parts.Length == 2)
                {
                    var typePart = parts[0].Trim();
                    var memberPart = parts[1].Trim();

                    candidates.AddRange(FindMembersByTypeAndName(typePart, memberPart, contextManager, memberResolver));
                }
            }

            // Pattern: hex token like "0x06012345"
            if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && input.Length > 2)
            {
                var hexPart = input[2..];
                if (int.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out var token))
                {
                    candidates.AddRange(FindMembersByToken(token, contextManager, memberResolver));
                }
            }

            // Pattern: Simple name search
            if (candidates.Count == 0)
            {
                candidates.AddRange(FindMembersByName(input, contextManager, memberResolver));
            }

            // Limit results to avoid overwhelming response
            candidates = candidates.Take(10).ToList();

            if (candidates.Count == 1)
            {
                return new
                {
                    normalizedId = (string?)candidates[0].MemberId,
                    candidates = (MemberSummary[]?)null
                };
            }
            else if (candidates.Count > 1)
            {
                return new
                {
                    normalizedId = (string?)null,
                    candidates = (MemberSummary[]?)candidates.ToArray()
                };
            }
            else
            {
                throw new ArgumentException($"Could not normalize input '{input}' to any known member");
            }
        });
    }

    private static IEnumerable<MemberSummary> FindMembersByTypeAndName(string typePart, string memberPart, AssemblyContextManager contextManager, MemberResolver memberResolver)
    {
        var candidates = new List<MemberSummary>();

        // Find types that match the type part
        var allTypes = contextManager.GetAllTypes();
        var matchingTypes = allTypes.Where(t =>
             t.Name.Equals(typePart, StringComparison.OrdinalIgnoreCase) ||
             t.FullName.Equals(typePart, StringComparison.OrdinalIgnoreCase) ||
             t.FullName.EndsWith("." + typePart, StringComparison.OrdinalIgnoreCase));

        foreach (var type in matchingTypes)
        {
            // Find members with matching names
            var members = GetAllMembers(type);
            var matchingMembers = members.Where(m =>
                 m.Name.Equals(memberPart, StringComparison.OrdinalIgnoreCase));

            foreach (var member in matchingMembers)
            {
                candidates.Add(CreateMemberSummary(member, memberResolver));
            }
        }

        return candidates;
    }

    private static IEnumerable<MemberSummary> FindMembersByToken(int token, AssemblyContextManager contextManager, MemberResolver memberResolver)
    {
        var candidates = new List<MemberSummary>();

        // TODO: Implement token-based member resolution
        //
        // This is a simplified token search - in a full implementation,
        // you would use the metadata token to directly resolve the member
        // For now, we'll skip this complex approach
        return candidates;
    }

    private static IEnumerable<MemberSummary> FindMembersByName(string name, AssemblyContextManager contextManager, MemberResolver memberResolver)
    {
        var candidates = new List<MemberSummary>();

        // Search types by name
        var allTypes = contextManager.GetAllTypes();
        var matchingTypes = allTypes.Where(t =>
             t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        foreach (var type in matchingTypes.Take(5)) // Limit to avoid too many results
        {
            candidates.Add(CreateTypeSummary(type, memberResolver));
        }

        // Search members by name
        foreach (var type in allTypes.Take(100)) // Limit types to search to avoid performance issues
        {
            var members = GetAllMembers(type);
            var matchingMembers = members.Where(m =>
                 m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            foreach (var member in matchingMembers.Take(5)) // Limit members per type
            {
                candidates.Add(CreateMemberSummary(member, memberResolver));
            }

            if (candidates.Count >= 10)
                break; // Overall limit
        }

        return candidates;
    }

    private static IEnumerable<IMember> GetAllMembers(ITypeDefinition type)
    {
        return type.Methods.Cast<IMember>()
             .Concat(type.Fields)
             .Concat(type.Properties)
             .Concat(type.Events);
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
}

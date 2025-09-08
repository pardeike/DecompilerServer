using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

[McpServerToolType]
public static class ResolveMemberIdTool
{
    [McpServerTool, Description("Resolve a memberId and return a one-line summary for quick validation.")]
    public static string ResolveMemberId(string memberId)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var memberResolver = ServiceLocator.MemberResolver;
            var contextManager = ServiceLocator.ContextManager;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var entity = memberResolver.ResolveMember(memberId);
            if (entity == null)
            {
                throw new ArgumentException($"Member ID '{memberId}' could not be resolved");
            }

            // Create a member summary based on the entity type
            if (entity is ITypeDefinition type)
            {
                return new MemberSummary
                {
                    MemberId = memberResolver.GenerateMemberId(entity),
                    Name = type.Name,
                    FullName = type.FullName,
                    Kind = "Type",
                    DeclaringType = type.DeclaringType?.FullName,
                    Namespace = type.Namespace,
                    Signature = memberResolver.GetMemberSignature(entity),
                    Accessibility = type.Accessibility.ToString(),
                    IsStatic = type.IsStatic,
                    IsAbstract = type.IsAbstract
                };
            }
            else if (entity is IMember member)
            {
                return new MemberSummary
                {
                    MemberId = memberResolver.GenerateMemberId(entity),
                    Name = member.Name,
                    FullName = member.FullName,
                    Kind = GetMemberKind(member),
                    DeclaringType = member.DeclaringType?.FullName,
                    Namespace = member.DeclaringType?.Namespace,
                    Signature = memberResolver.GetMemberSignature(entity),
                    Accessibility = member.Accessibility.ToString(),
                    IsStatic = member.IsStatic,
                    IsAbstract = member.IsAbstract,
                    IsVirtual = member.IsVirtual
                };
            }
            else
            {
                // Fallback for other entity types
                return new MemberSummary
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
                };
            }
        });
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

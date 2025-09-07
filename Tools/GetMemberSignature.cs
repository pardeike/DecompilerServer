using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

public static class GetMemberSignatureTool
{
    [McpServerTool, Description("Quick signature preview for any member.")]
    public static string GetMemberSignature(string memberId)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var entity = memberResolver.ResolveMember(memberId);
            if (entity == null)
            {
                throw new ArgumentException($"Member ID '{memberId}' could not be resolved");
            }

            var signature = memberResolver.GetMemberSignature(entity);

            // Create a minimal member summary
            var summary = CreateMemberSummary(entity, memberResolver);

            var result = new
            {
                summary = summary,
                signature = signature
            };

            return result;
        });
    }

    private static MemberSummary CreateMemberSummary(IEntity entity, MemberResolver memberResolver)
    {
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

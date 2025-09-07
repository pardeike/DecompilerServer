using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

public static class GetMemberDetailsTool
{
    [McpServerTool, Description("Detailed metadata for a member: attributes, docs, inheritance links.")]
    public static string GetMemberDetails(string memberId)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;
            var inheritanceAnalyzer = ServiceLocator.InheritanceAnalyzer;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var entity = memberResolver.ResolveMember(memberId);
            if (entity == null)
            {
                throw new ArgumentException($"Member ID '{memberId}' could not be resolved");
            }

            var details = new MemberDetails
            {
                MemberId = memberResolver.GenerateMemberId(entity),
                Name = entity.Name,
                FullName = entity.FullName,
                Kind = GetEntityKind(entity),
                Signature = memberResolver.GetMemberSignature(entity),
                DeclaringType = (entity as IMember)?.DeclaringType?.FullName ?? (entity as ITypeDefinition)?.DeclaringType?.FullName,
                Namespace = (entity as IMember)?.DeclaringType?.Namespace ?? (entity as ITypeDefinition)?.Namespace,
                Accessibility = GetAccessibility(entity),
                IsStatic = GetIsStatic(entity),
                IsAbstract = GetIsAbstract(entity),
                IsVirtual = GetIsVirtual(entity),
                Attributes = ExtractAttributes(entity),
                XmlDoc = ExtractXmlDocumentation(entity),
                BaseDefinitionId = GetBaseDefinitionId(entity, memberResolver),
                OverrideIds = GetOverrideIds(entity, inheritanceAnalyzer, memberResolver),
                ImplementorIds = GetImplementorIds(entity, inheritanceAnalyzer, memberResolver)
            };

            return details;
        });
    }

    private static string GetEntityKind(IEntity entity)
    {
        return entity switch
        {
            ITypeDefinition => "Type",
            IMethod method when method.IsConstructor => "Constructor",
            IMethod => "Method",
            IField => "Field",
            IProperty => "Property",
            IEvent => "Event",
            _ => "Unknown"
        };
    }

    private static string GetAccessibility(IEntity entity)
    {
        return entity switch
        {
            IMember member => member.Accessibility.ToString(),
            ITypeDefinition type => type.Accessibility.ToString(),
            _ => "Unknown"
        };
    }

    private static bool GetIsStatic(IEntity entity)
    {
        return entity switch
        {
            IMember member => member.IsStatic,
            ITypeDefinition type => type.IsStatic,
            _ => false
        };
    }

    private static bool GetIsAbstract(IEntity entity)
    {
        return entity switch
        {
            IMember member => member.IsAbstract,
            ITypeDefinition type => type.IsAbstract,
            _ => false
        };
    }

    private static bool GetIsVirtual(IEntity entity)
    {
        return entity switch
        {
            IMember member => member.IsVirtual,
            _ => false
        };
    }

    private static List<AttributeInfo>? ExtractAttributes(IEntity entity)
    {
        var attributes = entity.GetAttributes();
        if (!attributes.Any())
            return null;

        return attributes.Select(attr => new AttributeInfo
        {
            FullName = attr.AttributeType.FullName,
            ConstructorArgs = ExtractConstructorArgs(attr)
        }).ToList();
    }

    private static List<object>? ExtractConstructorArgs(IAttribute attribute)
    {
        // Extract simple constructor arguments
        var args = new List<object>();

        foreach (var arg in attribute.FixedArguments)
        {
            // Only extract simple types to avoid complex serialization
            var value = arg.Value;
            if (value is string || value is int || value is bool || value is double || value is float || value == null)
            {
                args.Add(value ?? "null");
            }
            else
            {
                args.Add($"[{value?.GetType().Name ?? "null"}]");
            }
        }

        return args.Any() ? args : null;
    }

    private static string? ExtractXmlDocumentation(IEntity entity)
    {
        // Basic XML documentation extraction
        // In a full implementation, you would use the actual XML documentation provider
        // For now, we'll return null as the full XML documentation system would be complex
        return null;
    }

    private static string? GetBaseDefinitionId(IEntity entity, MemberResolver memberResolver)
    {
        if (entity is IMember member && member.IsOverride)
        {
            var baseDefinition = member.MemberDefinition;
            if (baseDefinition != null && baseDefinition != member)
            {
                return memberResolver.GenerateMemberId(baseDefinition);
            }
        }
        return null;
    }

    private static List<string>? GetOverrideIds(IEntity entity, InheritanceAnalyzer inheritanceAnalyzer, MemberResolver memberResolver)
    {
        if (entity is IMethod method)
        {
            var methodId = memberResolver.GenerateMemberId(method);
            var overrides = inheritanceAnalyzer.GetOverrides(methodId);
            var overrideIds = overrides.Select(o => o.MemberId).ToList();
            return overrideIds.Any() ? overrideIds : null;
        }
        return null;
    }

    private static List<string>? GetImplementorIds(IEntity entity, InheritanceAnalyzer inheritanceAnalyzer, MemberResolver memberResolver)
    {
        if (entity is ITypeDefinition type && type.Kind == TypeKind.Interface)
        {
            var typeId = memberResolver.GenerateMemberId(type);
            var implementors = inheritanceAnalyzer.FindImplementors(typeId, limit: 100);
            var implementorIds = implementors.Select(i => i.MemberId).ToList();
            return implementorIds.Any() ? implementorIds : null;
        }
        return null;
    }
}

using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

public static class GetAstOutlineTool
{
    [McpServerTool, Description("AST outline: lightweight tree summary for a member for quick orientation.")]
    public static string GetAstOutline(string memberId, int maxDepth = 2)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var member = memberResolver.ResolveMember(memberId);
            if (member == null)
            {
                throw new ArgumentException($"Invalid member ID: {memberId}");
            }

            // For now, provide a simplified outline based on member metadata
            // Full AST parsing would require more complex implementation
            var outline = GenerateSimplifiedOutline(member, maxDepth);

            var result = new
            {
                memberId = memberId,
                memberName = member.Name,
                memberKind = GetMemberKind(member),
                outline = outline,
                maxDepth = maxDepth,
                note = "Simplified outline based on metadata - full AST parsing would require additional implementation"
            };

            return result;
        });
    }

    private static object GenerateSimplifiedOutline(IEntity member, int maxDepth)
    {
        try
        {
            if (member is IMethod method)
            {
                return GenerateMethodOutline(method, maxDepth);
            }
            else if (member is ITypeDefinition type)
            {
                return GenerateTypeOutline(type, maxDepth);
            }
            else if (member is IField field)
            {
                return new
                {
                    kind = "Field",
                    name = field.Name,
                    type = field.ReturnType.Name,
                    accessibility = field.Accessibility.ToString(),
                    isStatic = field.IsStatic,
                    children = new object[0]
                };
            }
            else if (member is IProperty property)
            {
                var children = new List<object>();
                
                if (property.Getter != null)
                {
                    children.Add(new
                    {
                        kind = "Getter",
                        name = "get",
                        accessibility = property.Getter.Accessibility.ToString()
                    });
                }
                
                if (property.Setter != null)
                {
                    children.Add(new
                    {
                        kind = "Setter", 
                        name = "set",
                        accessibility = property.Setter.Accessibility.ToString()
                    });
                }

                return new
                {
                    kind = "Property",
                    name = property.Name,
                    type = property.ReturnType.Name,
                    accessibility = property.Accessibility.ToString(),
                    isStatic = property.IsStatic,
                    children = children.ToArray()
                };
            }
            else
            {
                return new
                {
                    kind = GetMemberKind(member),
                    name = member.Name,
                    fullName = member.FullName,
                    children = new object[0]
                };
            }
        }
        catch (Exception ex)
        {
            return new
            {
                kind = GetMemberKind(member),
                name = member.Name,
                error = $"Outline generation failed: {ex.Message}",
                children = new object[0]
            };
        }
    }

    private static object GenerateMethodOutline(IMethod method, int maxDepth)
    {
        var children = new List<object>();

        // Add parameters as children if we have depth for it
        if (maxDepth > 0)
        {
            for (int i = 0; i < method.Parameters.Count; i++)
            {
                var param = method.Parameters[i];
                children.Add(new
                {
                    kind = "Parameter",
                    name = param.Name ?? $"param{i}",
                    type = param.Type.Name,
                    index = i
                });
            }
        }

        return new
        {
            kind = method.IsConstructor ? "Constructor" : "Method",
            name = method.Name,
            returnType = method.ReturnType.Name,
            accessibility = method.Accessibility.ToString(),
            isStatic = method.IsStatic,
            isAbstract = method.IsAbstract,
            isVirtual = method.IsVirtual,
            parameterCount = method.Parameters.Count,
            children = children.ToArray()
        };
    }

    private static object GenerateTypeOutline(ITypeDefinition type, int maxDepth)
    {
        var children = new List<object>();

        // Add members as children if we have depth for it
        if (maxDepth > 0)
        {
            // Add constructors
            foreach (var ctor in type.Methods.Where(m => m.IsConstructor).Take(5))
            {
                children.Add(new
                {
                    kind = "Constructor",
                    name = ".ctor",
                    accessibility = ctor.Accessibility.ToString(),
                    parameterCount = ctor.Parameters.Count
                });
            }

            // Add methods
            foreach (var method in type.Methods.Where(m => !m.IsConstructor).Take(10))
            {
                children.Add(new
                {
                    kind = "Method",
                    name = method.Name,
                    accessibility = method.Accessibility.ToString(),
                    isStatic = method.IsStatic,
                    returnType = method.ReturnType.Name,
                    parameterCount = method.Parameters.Count
                });
            }

            // Add fields
            foreach (var field in type.Fields.Take(10))
            {
                children.Add(new
                {
                    kind = "Field",
                    name = field.Name,
                    accessibility = field.Accessibility.ToString(),
                    isStatic = field.IsStatic,
                    type = field.ReturnType.Name
                });
            }

            // Add properties
            foreach (var prop in type.Properties.Take(10))
            {
                children.Add(new
                {
                    kind = "Property",
                    name = prop.Name,
                    accessibility = prop.Accessibility.ToString(),
                    isStatic = prop.IsStatic,
                    type = prop.ReturnType.Name,
                    hasGetter = prop.Getter != null,
                    hasSetter = prop.Setter != null
                });
            }
        }

        return new
        {
            kind = type.Kind.ToString(),
            name = type.Name,
            fullName = type.FullName,
            accessibility = type.Accessibility.ToString(),
            isStatic = type.IsStatic,
            isAbstract = type.IsAbstract,
            isSealed = type.IsSealed,
            baseTypeCount = type.DirectBaseTypes.Count(),
            memberCount = type.Members.Count(),
            children = children.ToArray()
        };
    }

    private static string GetMemberKind(IEntity member)
    {
        return member switch
        {
            IMethod method when method.IsConstructor => "Constructor",
            IMethod => "Method",
            IField => "Field",
            IProperty => "Property",
            IEvent => "Event",
            ITypeDefinition type => type.Kind.ToString(),
            _ => "Unknown"
        };
    }
}

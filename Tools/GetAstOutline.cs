using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

[McpServerToolType]
public static class GetAstOutlineTool
{
    [McpServerTool(ReadOnly = true), Description("Metadata-based outline for quick orientation. This is not a full statement-level AST.")]
    public static string GetAstOutline(string memberId, int maxDepth = 2, string? contextAlias = null, int limit = 50, string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            using var session = ToolSessionRouter.GetForMember(memberId, contextAlias);
            var contextManager = session.ContextManager;
            var memberResolver = session.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var member = ToolValidation.ResolveMemberOrThrow(session, memberId);

            // For now, provide a simplified outline based on member metadata
            // Full AST parsing would require more complex implementation
            if (maxDepth < 0 || limit <= 0)
                throw new ArgumentException("maxDepth must be non-negative and limit must be positive.");
            var outline = member is ITypeDefinition type
                ? GenerateTypeOutline(type, maxDepth, memberResolver, limit, cursor)
                : GenerateSimplifiedOutline(member, maxDepth);

            var result = new
            {
                memberId = memberId,
                memberName = member.Name,
                memberKind = GetMemberKind(member),
                outline = outline,
                maxDepth = maxDepth,
                isFullAst = false,
                note = "Simplified outline based on metadata. This endpoint does not currently expose a full statement-level AST."
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
                throw new InvalidOperationException("Type outlines require the member resolver.");
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

    private static object GenerateTypeOutline(ITypeDefinition type, int maxDepth, MemberResolver resolver, int limit, string? cursor)
    {
        var members = TypeSurfaceComparer.GetDirectMembers(type).Where(member => !TypeSurfaceComparer.IsCompilerGenerated(member))
            .OrderBy(member => member.Name, StringComparer.Ordinal).ThenBy(member => resolver.GetMemberSignature(member), StringComparer.Ordinal).ToArray();
        var page = UsageAnalyzer.Page(members, limit, cursor);
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
            memberCount = members.Length,
            children = maxDepth == 0 ? Array.Empty<object>() : page.Items.Select(member => (object)new
            {
                memberId = resolver.GenerateMemberId(member),
                name = member.Name,
                kind = member is IMethod { IsConstructor: true } ? "Constructor" : TypeSurfaceComparer.ToDisplayKind(TypeSurfaceComparer.GetNormalizedMemberKind(member)),
                signature = resolver.GetMemberSignature(member)
            }).ToArray(),
            hasMore = maxDepth > 0 && page.HasMore,
            nextCursor = maxDepth > 0 ? page.NextCursor : null,
            totalEstimate = members.Length
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

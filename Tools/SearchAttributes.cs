using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

public static class SearchAttributesTool
{
    [McpServerTool, Description("Find members decorated with a specific attribute type.")]
    public static string SearchAttributes(string attributeFullName, string? kind = null, int limit = 100, string? cursor = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var compilation = contextManager.GetCompilation();
            var allTypes = contextManager.GetAllTypes();

            var matchingMembers = new List<MemberSummary>();

            // Parse cursor for pagination
            var startIndex = 0;
            if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
            {
                startIndex = cursorIndex;
            }

            // Search through all types and their members
            foreach (var type in allTypes.Skip(startIndex))
            {
                if (matchingMembers.Count >= limit)
                    break;

                // Check the type itself if no kind filter or kind is "type"
                if (string.IsNullOrEmpty(kind) || kind.Equals("type", StringComparison.OrdinalIgnoreCase))
                {
                    if (HasAttribute(type, attributeFullName))
                    {
                        matchingMembers.Add(CreateTypeSummary(type, memberResolver));
                    }
                }

                // Check methods
                if (string.IsNullOrEmpty(kind) || kind.Equals("method", StringComparison.OrdinalIgnoreCase) || kind.Equals("constructor", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var method in type.Methods)
                    {
                        if (matchingMembers.Count >= limit) break;

                        if (HasAttribute(method, attributeFullName))
                        {
                            // Filter by constructor vs method if specified
                            if (kind?.Equals("constructor", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                if (method.IsConstructor)
                                    matchingMembers.Add(CreateMemberSummary(method, memberResolver));
                            }
                            else if (kind?.Equals("method", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                if (!method.IsConstructor)
                                    matchingMembers.Add(CreateMemberSummary(method, memberResolver));
                            }
                            else
                            {
                                matchingMembers.Add(CreateMemberSummary(method, memberResolver));
                            }
                        }
                    }
                }

                // Check fields
                if (string.IsNullOrEmpty(kind) || kind.Equals("field", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var field in type.Fields)
                    {
                        if (matchingMembers.Count >= limit) break;

                        if (HasAttribute(field, attributeFullName))
                        {
                            matchingMembers.Add(CreateMemberSummary(field, memberResolver));
                        }
                    }
                }

                // Check properties
                if (string.IsNullOrEmpty(kind) || kind.Equals("property", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var property in type.Properties)
                    {
                        if (matchingMembers.Count >= limit) break;

                        if (HasAttribute(property, attributeFullName))
                        {
                            matchingMembers.Add(CreateMemberSummary(property, memberResolver));
                        }
                    }
                }

                // Check events
                if (string.IsNullOrEmpty(kind) || kind.Equals("event", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var evt in type.Events)
                    {
                        if (matchingMembers.Count >= limit) break;

                        if (HasAttribute(evt, attributeFullName))
                        {
                            matchingMembers.Add(CreateMemberSummary(evt, memberResolver));
                        }
                    }
                }
            }

            // Calculate pagination info
            var hasMore = matchingMembers.Count >= limit;
            var nextCursor = hasMore ? (startIndex + allTypes.Count()).ToString() : null;

            var result = new SearchResult<MemberSummary>
            {
                Items = matchingMembers,
                HasMore = hasMore,
                NextCursor = nextCursor,
                TotalEstimate = matchingMembers.Count
            };

            return result;
        });
    }

    private static bool HasAttribute(IEntity entity, string attributeFullName)
    {
        return entity.GetAttributes().Any(attr =>
            attr.AttributeType.FullName.Equals(attributeFullName, StringComparison.OrdinalIgnoreCase) ||
            attr.AttributeType.FullName.Contains(attributeFullName, StringComparison.OrdinalIgnoreCase));
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

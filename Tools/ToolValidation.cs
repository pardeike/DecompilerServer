using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

internal static class ToolValidation
{
    public static IEntity ResolveMemberOrThrow(ToolSessionView session, string memberId, string? expectedKind = null)
    {
        var entity = session.MemberResolver.ResolveMember(memberId);
        if (entity == null)
        {
            throw SymbolResolutionDiagnostics.CreateUnresolvedMemberError(
                memberId,
                session.ContextManager,
                session.MemberResolver,
                expectedKind);
        }

        return entity;
    }

    public static ITypeDefinition ResolveTypeOrThrow(ToolSessionView session, string typeId)
    {
        var entity = ResolveMemberOrThrow(session, typeId, "type");
        if (entity is ITypeDefinition type)
            return type;

        throw SymbolResolutionDiagnostics.CreateWrongKindError(typeId, "type", entity, session.MemberResolver);
    }

    public static IMethod ResolveMethodOrThrow(ToolSessionView session, string methodId)
    {
        var entity = ResolveMemberOrThrow(session, methodId, "method");
        if (entity is IMethod method)
            return method;

        throw SymbolResolutionDiagnostics.CreateWrongKindError(methodId, "method", entity, session.MemberResolver);
    }
}

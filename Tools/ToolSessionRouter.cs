using DecompilerServer.Services;

namespace DecompilerServer;

internal sealed record ToolSessionView(
    string? ContextAlias,
    AssemblyContextManager ContextManager,
    MemberResolver MemberResolver,
    DecompilerService DecompilerService,
    UsageAnalyzer UsageAnalyzer,
    InheritanceAnalyzer InheritanceAnalyzer);

internal static class ToolSessionRouter
{
    public static ToolSessionView GetForContext(string? contextAlias = null)
    {
        var workspace = ServiceLocator.Workspace;
        if (workspace != null)
        {
            DecompilerSession session;
            if (!string.IsNullOrWhiteSpace(contextAlias))
            {
                if (!workspace.TryGetSession(contextAlias, out session!))
                    throw new InvalidOperationException($"Context alias '{contextAlias}' is not loaded.");
            }
            else
            {
                session = workspace.GetCurrentSession();
            }

            return FromSession(session);
        }

        return GetLegacyCurrent();
    }

    public static ToolSessionView GetForMember(string memberId, string? contextAlias = null)
    {
        var workspace = ServiceLocator.Workspace;
        if (workspace != null)
        {
            if (!string.IsNullOrWhiteSpace(contextAlias))
            {
                if (!workspace.TryGetSession(contextAlias, out var explicitSession))
                    throw new InvalidOperationException($"Context alias '{contextAlias}' is not loaded.");

                return FromSession(explicitSession);
            }

            return FromSession(workspace.ResolveSessionForMemberId(memberId));
        }

        return GetLegacyCurrent();
    }

    private static ToolSessionView FromSession(DecompilerSession session)
    {
        return new ToolSessionView(
            session.ContextAlias,
            session.ContextManager,
            session.MemberResolver,
            session.DecompilerService,
            session.UsageAnalyzer,
            session.InheritanceAnalyzer);
    }

    private static ToolSessionView GetLegacyCurrent()
    {
        return new ToolSessionView(
            ContextAlias: null,
            ContextManager: ServiceLocator.ContextManager,
            MemberResolver: ServiceLocator.MemberResolver,
            DecompilerService: ServiceLocator.DecompilerService,
            UsageAnalyzer: ServiceLocator.UsageAnalyzer,
            InheritanceAnalyzer: ServiceLocator.InheritanceAnalyzer);
    }
}

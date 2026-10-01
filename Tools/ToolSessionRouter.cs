using DecompilerServer.Services;

namespace DecompilerServer;

internal sealed record ToolSessionView(
    string? ContextAlias,
    AssemblyContextManager ContextManager,
    MemberResolver MemberResolver,
    DecompilerService DecompilerService,
    UsageAnalyzer UsageAnalyzer,
    InheritanceAnalyzer InheritanceAnalyzer,
    DecompilerSessionLease? Lease = null) : IDisposable
{
    private SearchService? _legacySearch;
    internal SearchService SearchService => Lease?.Session.SearchService ?? (_legacySearch ??= new SearchService(ContextManager, MemberResolver));
    public void Dispose() => Lease?.Dispose();
}

internal static class ToolSessionRouter
{
    public static ToolSessionView GetForContext(string? contextAlias = null)
    {
        var workspace = ServiceLocator.Workspace;
        if (workspace != null)
        {
            DecompilerSessionLease lease;
            if (!string.IsNullOrWhiteSpace(contextAlias))
            {
                lease = workspace.AcquireSession(contextAlias);
            }
            else
            {
                lease = workspace.AcquireCurrentSession();
            }

            return FromLease(lease);
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
                return FromLease(workspace.AcquireSession(contextAlias));
            }

            return FromLease(workspace.AcquireSessionForMemberId(memberId));
        }

        return GetLegacyCurrent();
    }

    public static ToolSessionView? TryGetCurrentLoaded()
    {
        var workspace = ServiceLocator.Workspace;
        if (workspace == null || !workspace.TryAcquireCurrentLoadedSession(out var lease))
            return null;

        return FromLease(lease);
    }

    private static ToolSessionView FromLease(DecompilerSessionLease lease)
    {
        var session = lease.Session;
        return new ToolSessionView(
            session.ContextAlias,
            session.ContextManager,
            session.MemberResolver,
            session.DecompilerService,
            session.UsageAnalyzer,
            session.InheritanceAnalyzer,
            lease);
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

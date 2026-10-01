using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

internal static class ReferenceQuery
{
    public static object Find(ToolSessionView owner, IEntity target, string input, bool callersOnly,
        string scope, int limit, string? cursor)
    {
        scope = scope.Trim().ToLowerInvariant();
        if (scope is not ("assembly" or "workspace"))
            throw new ArgumentException("scope must be 'assembly' or 'workspace'.", nameof(scope));
        limit = MemberSummaryModes.ClampLimit(limit, 100);
        var leases = scope == "workspace" ? ServiceLocator.Workspace?.AcquireLoadedSessions() : null;
        try
        {
            AnalysisPage<UsageReference> page;
            string[] searchedContexts;
            if (leases == null)
            {
                page = callersOnly ? owner.UsageAnalyzer.FindCallersPage(input, limit, cursor)
                    : owner.UsageAnalyzer.FindUsagesPage(input, limit, cursor);
                searchedContexts = [owner.ContextAlias ?? owner.ContextManager.Mvid!];
            }
            else
            {
                var items = leases.SelectMany(lease => lease.Session.UsageAnalyzer.FindUsagesOf(target))
                    .Where(item => !callersOnly || item.Kind is UsageKind.Call or UsageKind.NewObject)
                    .GroupBy(item => (item.InMember, item.Offset, item.Kind))
                    .Select(group => group.First())
                    .OrderBy(item => item.InMember, StringComparer.Ordinal).ThenBy(item => item.Offset).ToList();
                page = UsageAnalyzer.Page(items, limit, cursor);
                searchedContexts = leases.Select(lease => lease.Session.ContextAlias).ToArray();
            }
            return new { page.Items, page.HasMore, page.NextCursor, page.TotalEstimate, scope, searchedContexts };
        }
        finally
        {
            if (leases != null)
                foreach (var lease in leases)
                    lease.Dispose();
        }
    }

    public static List<CalleeReference> LinkLoadedTargets(List<CalleeReference> items)
    {
        var leases = ServiceLocator.Workspace?.AcquireLoadedSessions();
        if (leases == null)
            return items;
        try
        {
            return items.Select(item =>
            {
                if (item.CandidateMemberId == null)
                    return item;
                var target = leases.FirstOrDefault(lease =>
                    item.CandidateMemberId.StartsWith(lease.Session.ContextManager.Mvid!.Replace("-", ""), StringComparison.OrdinalIgnoreCase));
                if (target?.Session.ContextManager.FindMemberById(item.CandidateMemberId) == null)
                    return item;
                return item with { TargetMemberId = item.CandidateMemberId, Resolution = "workspace", Resolved = true };
            }).ToList();
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }
}

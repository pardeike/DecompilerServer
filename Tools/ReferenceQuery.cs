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
            return new { page.Items, page.HasMore, page.NextCursor, page.TotalEstimate, scope, searchedContexts,
                hint = BuildEmptyScopeHint(scope, page, owner) };
        }
        finally
        {
            if (leases != null)
                foreach (var lease in leases)
                    lease.Dispose();
        }
    }

    /// <summary>
    /// scope "assembly" only ever searches the one loaded assembly, so an empty result is ambiguous:
    /// the member may well be referenced from a dependent assembly that is not loaded. Say so instead
    /// of letting the caller conclude "nobody calls this".
    /// </summary>
    private static string? BuildEmptyScopeHint(string scope, AnalysisPage<UsageReference> page, ToolSessionView owner)
    {
        if (scope != "assembly" || page.Items.Count > 0)
            return null;

        var alias = owner.ContextAlias ?? owner.ContextManager.Mvid ?? "(current)";
        var sessions = ServiceLocator.Workspace?.AcquireLoadedSessions();
        try
        {
            var others = sessions?
                .Select(lease => lease.Session.ContextAlias)
                .Where(other => !string.Equals(other, alias, StringComparison.Ordinal))
                .OrderBy(other => other, StringComparer.Ordinal)
                .ToList() ?? [];

        return others.Count > 0
                ? $"No references inside assembly '{alias}'. {others.Count} other context(s) are loaded ({string.Join(", ", others)}); " +
                  "retry with scope:\"workspace\" to search them."
                : $"No references inside assembly '{alias}'. No other assembly is loaded, so cross-assembly references " +
                  "cannot be ruled out; load a dependent assembly and retry with scope:\"workspace\".";
        }
        finally
        {
            if (sessions != null)
                foreach (var lease in sessions)
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

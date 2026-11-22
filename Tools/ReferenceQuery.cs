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
            return new
            {
                page.Items,
                page.HasMore,
                page.NextCursor,
                page.TotalEstimate,
                scope,
                searchedContexts,
                hints = scope == "assembly" && cursor == null && page.Items.Count == 0
                    ? BuildEmptyAssemblyScopeHints(owner, owner.MemberResolver.GenerateMemberId(target), callersOnly)
                    : null
            };
        }
        finally
        {
            if (leases != null)
                foreach (var lease in leases)
                    lease.Dispose();
        }
    }

    // An empty assembly-scope result does not rule out references from other assemblies.
    private static ToolErrorHint[] BuildEmptyAssemblyScopeHints(ToolSessionView owner, string memberId, bool callersOnly)
    {
        var alias = owner.ContextAlias ?? owner.ContextManager.Mvid!;
        var others = ServiceLocator.Workspace?.ListContexts()
            .Select(context => context.ContextAlias)
            .Where(other => !string.Equals(other, alias, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        if (others.Count == 0)
            return
            [
                new ToolErrorHint(
                    "load_assembly",
                    new { makeCurrent = false },
                    $"No references in '{alias}' and no other context is loaded. Load dependent assemblies, then retry with scope \"workspace\".")
            ];

        object arguments = callersOnly
            ? new { methodId = memberId, scope = "workspace" }
            : new { memberId, scope = "workspace" };
        return
        [
            new ToolErrorHint(
                callersOnly ? "find_callers" : "find_usages",
                arguments,
                $"No references in '{alias}'. Workspace scope also searches the loaded contexts {string.Join(", ", others)}.")
        ];
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

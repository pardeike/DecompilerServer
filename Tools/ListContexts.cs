using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class ListContextsTool
{
    [McpServerTool(ReadOnly = true), Description("List resident contexts and the current alias. Set includeRegistered for a filtered, paged list of deferred aliases; use status for readiness.")]
    public static string ListContexts(bool? includeRegistered = null, int limit = 50, string? cursor = null, string? filter = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var workspace = ServiceLocator.Workspace;
            if (workspace == null)
            {
                return new WorkspaceContextsResult
                {
                    CurrentContextAlias = null,
                    Items = new List<WorkspaceContextInfo>()
                };
            }

            if (limit <= 0)
                throw new ArgumentException("limit must be positive.");
            var aliases = workspace.ListRegisteredAliases();
            var include = includeRegistered ?? !ResponseFormatter.IsCompact;
            var filtered = aliases.Where(alias => string.IsNullOrWhiteSpace(filter) || alias.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            var page = UsageAnalyzer.Page(filtered, limit, cursor);
            return new WorkspaceContextsResult
            {
                CurrentContextAlias = workspace.CurrentContextAlias,
                Items = workspace.ListContexts().ToList(),
                RegisteredAliases = include ? page.Items : null,
                RegisteredAliasCount = aliases.Count,
                HasMore = include ? page.HasMore : null,
                NextCursor = include ? page.NextCursor : null,
                TotalEstimate = include ? page.TotalEstimate : null
            };
        });
    }
}

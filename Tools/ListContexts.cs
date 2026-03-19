using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class ListContextsTool
{
    [McpServerTool, Description("List loaded assembly contexts and indicate which one is current.")]
    public static string ListContexts()
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

            return new WorkspaceContextsResult
            {
                CurrentContextAlias = workspace.CurrentContextAlias,
                Items = workspace.ListContexts().ToList()
            };
        });
    }
}

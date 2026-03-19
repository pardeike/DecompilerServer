using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class FindBaseTypesTool
{
    [McpServerTool, Description("Get base types and optionally implemented interfaces.")]
    public static string FindBaseTypes(string typeId, bool includeInterfaces = true, string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var session = ToolSessionRouter.GetForMember(typeId, contextAlias);
            var contextManager = session.ContextManager;
            var inheritanceAnalyzer = session.InheritanceAnalyzer;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var baseTypes = inheritanceAnalyzer.FindBaseTypes(typeId);
            var interfaces = includeInterfaces
                ? inheritanceAnalyzer.GetImplementations(typeId).ToList()
                : new List<MemberSummary>();

            return new
            {
                bases = baseTypes.ToList(),
                interfaces = interfaces
            };
        });
    }
}

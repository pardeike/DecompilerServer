using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class FindBaseTypesTool
{
    [McpServerTool, Description("Get base types and optionally implemented interfaces.")]
    public static string FindBaseTypes(string typeId, bool includeInterfaces = true)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var inheritanceAnalyzer = ServiceLocator.GetRequiredService<InheritanceAnalyzer>();

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

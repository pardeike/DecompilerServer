using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class FindCalleesTool
{
	[McpServerTool, Description("List direct callees invoked by a method.")]
	public static string FindCallees(string methodId, int limit = 100, string? cursor = null)
	{
		/*
		Behavior:
		- Scan target method IL for invocation instructions. Resolve targets to handles.
		- Return SearchResult<MemberSummary>.
		*/
		return "TODO";
	}
}

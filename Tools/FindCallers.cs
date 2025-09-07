using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class FindCallersTool
{
	[McpServerTool, Description("List direct callers of a method.")]
	public static string FindCallers(string methodId, int limit = 100, string? cursor = null)
	{
		/*
		Behavior:
		- Reuse FindUsages specialized for call/callvirt/newobj edges.
		- Return SearchResult<MemberSummary> of caller members.
		*/
		return "TODO";
	}
}

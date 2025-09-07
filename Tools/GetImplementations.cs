using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetImplementationsTool
{
	[McpServerTool, Description("Find implementations of an interface or interface method.")]
	public static string GetImplementations(string interfaceTypeOrMethodId, int limit = 100, string? cursor = null)
	{
		/*
		Behavior:
		- If typeId is interface type, find all types implementing it; else if methodId is interface method, find concrete implementations.
		- Return SearchResult<MemberSummary>.
		*/
		return "TODO";
	}
}

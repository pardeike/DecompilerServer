using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetOverridesTool
{
	[McpServerTool, Description("Find override chain of a virtual method. Base definition and overrides.")]
	public static string GetOverrides(string methodId)
	{
		/*
		Behavior:
		- Resolve methodId. Get base definition. Walk derived types to collect overrides.
		- Return { baseDefinition: MemberSummary, overrides: MemberSummary[] }.
		*/
		return "TODO";
	}
}

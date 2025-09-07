using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetXmlDocTool
{
	[McpServerTool, Description("Get raw XML doc for a member if available.")]
	public static string GetXmlDoc(string memberId)
	{
		/*
		Behavior:
		- Resolve member. Return raw XML documentation string if present, else null.
		*/
		return "TODO";
	}
}

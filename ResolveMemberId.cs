using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class ResolveMemberIdTool
{
	[McpServerTool, Description("Resolve a memberId and return a one-line summary for quick validation.")]
	public static string ResolveMemberId(string memberId)
	{
		/*
		Behavior:
		- Resolve and return MemberSummary or error if unknown.
		*/
		return "TODO";
	}
}

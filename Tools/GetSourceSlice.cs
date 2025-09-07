using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetSourceSliceTool
{
	[McpServerTool, Description("Return a line range of the decompiled source for a member.")]
	public static string GetSourceSlice(string memberId, int startLine, int endLine, bool includeLineNumbers = false, int context = 0)
	{
		/*
		Behavior:
		- Ensure document in cache (call decompile if missing).
		- Expand start/end by 'context' within bounds.
		- Return SourceSlice with exact lines and optional prefixed line numbers.
		- Validate ranges and cap large requests.
		*/
		return "TODO";
	}
}

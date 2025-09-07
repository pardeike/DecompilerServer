using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class BatchGetDecompiledSourceTool
{
	[McpServerTool, Description("Fetch multiple members' decompiled source in one call with size caps.")]
	public static string BatchGetDecompiledSource(string[] memberIds, int maxTotalChars = 200_000)
	{
		/*
		Behavior:
		- For each memberId, decompile if needed and append to output until maxTotalChars reached.
		- Return array of { doc: SourceDocument, firstSlice: SourceSlice } to give immediate text plus metadata.
		- Include truncated flag if cap reached.
		*/
		return "TODO";
	}
}

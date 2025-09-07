using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class PlanChunkingTool
{
	[McpServerTool, Description("Plan line-range chunks for a member's source for LLM-friendly paging.")]
	public static string PlanChunking(string memberId, int targetChunkSize = 6000, int overlap = 2)
	{
		/*
		Behavior:
		- Use cached document length and average line length to partition into ranges producing roughly targetChunkSize characters.
		- Include 'overlap' lines between chunks.
		- Return: { memberId, chunks: [ { startLine, endLine }, ... ] }.
		*/
		return "TODO";
	}
}

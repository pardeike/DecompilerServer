using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetServerStatsTool
{
	[McpServerTool, Description("Basic health and timing info for the server.")]
	public static string GetServerStats()
	{
		/*
		Behavior:
		- Return Stats plus memory footprint estimates of caches and index freshness flags.
		*/
		return "TODO";
	}
}

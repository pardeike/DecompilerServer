using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class StatusTool
{
	[McpServerTool, Description("Get current server status, including assembly MVID and cache stats.")]
	public static string Status()
	{
		/*
		Goal: Report whether an assembly is loaded and key counters.

		Output:
		{
		  loaded: bool,
		  mvid?: string,
		  assemblyPath?: string,
		  startedAtUnix?: long,
		  settings?: { ... decompiler flags ... },
		  stats?: Stats,
		  indexes: { namespaces: int, types: int, nameIndexReady: bool, stringLiteralIndexReady: bool }
		}
		*/
		return "TODO";
	}
}
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class WarmIndexTool
{
	[McpServerTool, Description("Optionally precompute heavier indexes (string literals, attribute hits). Time-boxed.")]
	public static string WarmIndex(bool deep = false, double maxSeconds = 5.0)
	{
		/*
		Goal: Time-boxed background-style index build within the request.

		Behavior:
		- Within maxSeconds, build or extend:
		  * string literal index (memberId -> literals)
		  * attribute index (attribute full name -> memberIds)
		  * quick callers map for hot methods (heuristic: size/complexity)
		- Report progress:
		  { status: "ok", deepRequested: bool, elapsedMs: int, built: ["strings","attributes","callers?"] }
		*/
		return "TODO";
	}
}
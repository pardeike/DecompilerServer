using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class ClearCachesTool
{
    [McpServerTool, Description("Clear caches and indexes. Scope: 'all' | 'source' | 'usages' | 'attributes'.")]
    public static string ClearCaches(string scope = "all")
    {
        /*
		Goal: Free memory and force recomputation as needed.

		Behavior:
		- Validate scope, clear corresponding dictionaries.
		- Return { status: "ok", cleared: "<scope>" }.
		*/
        return "TODO";
    }
}
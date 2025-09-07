using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class UnloadTool
{
    [McpServerTool, Description("Unload assembly and free all caches and indexes.")]
    public static string Unload()
    {
        /*
		Goal: Dispose the global decompiler context and clear caches.

		Behavior:
		- Acquire write lock, dispose PEFile and resolver, clear dictionaries, reset stats.
		- Return { status: "ok" }.
		*/
        return "TODO";
    }
}
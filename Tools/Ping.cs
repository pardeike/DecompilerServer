using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class PingTool
{
    [McpServerTool, Description("Connectivity check. Returns 'pong' and current MVID if loaded.")]
    public static string Ping()
    {
        /*
		Behavior:
		- Return { pong: true, mvid?: string, timeUnix: long }.
		*/
        return "TODO";
    }
}

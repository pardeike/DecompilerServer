using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GenerateDetourStubTool
{
    [McpServerTool, Description("Generate a detour/stub method that calls the original, suitable for patch testing.")]
    public static string GenerateDetourStub(string memberId)
    {
        /*
		Behavior:
		- Emit a static wrapper method with identical signature that logs entry/exit and delegates to original via MethodInfo or Harmony delegate helper.
		- Include notes on generics and ref/out safety.
		*/
        return "TODO";
    }
}

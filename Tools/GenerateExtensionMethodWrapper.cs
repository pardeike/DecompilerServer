using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GenerateExtensionMethodWrapperTool
{
    [McpServerTool, Description("Generate an extension method wrapper for an instance method to ease call sites in mods.")]
    public static string GenerateExtensionMethodWrapper(string memberId)
    {
        /*
		Behavior:
		- If target is instance method, create a public static extension method on declaring type (or interface if applicable).
		- Preserve generic parameters and constraints in the wrapper.
		- Return code string.
		*/
        return "TODO";
    }
}

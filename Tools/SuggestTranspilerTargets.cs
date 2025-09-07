using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class SuggestTranspilerTargetsTool
{
    [McpServerTool, Description("Suggest candidate IL offsets and patterns for transpiler insertion points.")]
    public static string SuggestTranspilerTargets(string memberId, int maxHints = 10)
    {
        /*
		Behavior:
		- Analyze IL and produce up to maxHints anchors: { offset, opcode, operandSummary, nearbyOps[], rationale }.
		- Include example transpiler snippet showing a CodeInstruction search pattern and insertion.
		*/
        return "TODO";
    }
}

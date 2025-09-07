using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class FindBaseTypesTool
{
    [McpServerTool, Description("Get base types and optionally implemented interfaces.")]
    public static string FindBaseTypes(string typeId, bool includeInterfaces = true)
    {
        /*
		Behavior:
		- Resolve typeId. Return ordered list: base class chain and interfaces (if requested).
		- Output: { bases: MemberSummary[], interfaces?: MemberSummary[] }.
		*/
        return "TODO";
    }
}

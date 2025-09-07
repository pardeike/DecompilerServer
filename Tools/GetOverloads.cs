using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetOverloadsTool
{
    [McpServerTool, Description("Find overloads for a method name within its declaring type.")]
    public static string GetOverloads(string memberId)
    {
        /*
		Behavior:
		- Resolve memberId to a method (or property accessor). Enumerate same-name methods in declaring type.
		- Return SearchResult<MemberSummary> with overloads ordered by parameter count then specificity.
		*/
        return "TODO";
    }
}

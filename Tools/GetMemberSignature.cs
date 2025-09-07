using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetMemberSignatureTool
{
    [McpServerTool, Description("Quick signature preview for any member.")]
    public static string GetMemberSignature(string memberId)
    {
        /*
		Behavior:
		- Resolve memberId. Produce concise C# signature string and minimal MemberSummary.
		- Output: { summary: MemberSummary, signature: string }.
		*/
        return "TODO";
    }
}

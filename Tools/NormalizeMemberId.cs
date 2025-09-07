using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class NormalizeMemberIdTool
{
    [McpServerTool, Description("Normalize a possibly partial or human-entered identifier into a canonical memberId.")]
    public static string NormalizeMemberId(string input)
    {
        /*
		Behavior:
		- Accept forms like "Verse.Pawn:Tick", "Pawn.Tick", tokens like "0x06012345", or full IDs.
		- Attempt to resolve uniquely. If ambiguous, return candidates.
		- Output: { normalizedId?: string, candidates?: MemberSummary[] }.
		*/
        return "TODO";
    }
}

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class FindUsagesTool
{
    [McpServerTool, Description("Find usages of a member across the assembly. Time-box and paginate.")]
    public static string FindUsages(string memberId, int limit = 100, string? cursor = null)
    {
        /*
		Behavior:
		- Identify metadata token(s) for the target.
		- Iterate candidate methods (heuristic order: smaller first; optionally from prebuilt callers map).
		- Inspect IL for instructions referencing target token (calls, ld/st fld, newobj).
		- Produce UsageRef { InMember, Kind, Line?, Snippet? }. If line unknown, omit.
		- Paginate. Respect time budgets per call.

		Helper methods to use:
		- MemberResolver.ResolveMember() to validate and resolve member ID
		- UsageAnalyzer.FindUsages() for IL-based usage analysis
		- ResponseFormatter.TryExecute() for error handling
		- ResponseFormatter.SearchResult() for paginated response formatting
		*/
        return "TODO";
    }
}

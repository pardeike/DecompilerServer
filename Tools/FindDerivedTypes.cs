using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class FindDerivedTypesTool
{
    [McpServerTool, Description("Find derived types of a base class. Optionally include indirect.")]
    public static string FindDerivedTypes(string baseTypeId, bool transitive = true, int limit = 100, string? cursor = null)
    {
        /*
		Behavior:
		- Resolve base type. Enumerate direct or transitive derivatives via type system hierarchy.
		- Return SearchResult<MemberSummary>.
		*/
        return "TODO";
    }
}

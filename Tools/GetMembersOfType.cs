using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetMembersOfTypeTool
{
    [McpServerTool, Description("List members of a given type with filters and pagination.")]
    public static string GetMembersOfType(string typeId, string? kind = null, string? accessibility = null, bool? isStatic = null, bool includeInherited = false, int limit = 100, string? cursor = null)
    {
        /*
		Behavior:
		- Resolve typeId to ITypeDefinition.
		- Enumerate members. If includeInherited, include base members with override/hidden markers.
		- Filter by kind/accessibility/static.
		- Return SearchResult<MemberSummary>.
		*/
        return "TODO";
    }
}

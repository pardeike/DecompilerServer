using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class SearchAttributesTool
{
    [McpServerTool, Description("Find members decorated with a specific attribute type.")]
    public static string SearchAttributes(string attributeFullName, string? kind = null, int limit = 100, string? cursor = null)
    {
        /*
		Behavior:
		- attributeFullName must be fully-qualified (e.g., "Verse.StaticConstructorOnStartup").
		- Use attribute index if available or scan member metadata lazily.
		- Optional kind filter.
		- Return SearchResult<MemberSummary>.
		*/
        return "TODO";
    }
}

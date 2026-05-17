using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

[McpServerToolType]
public static class ListMembersTool
{
    [McpServerTool, Description("Compatibility alias for get_members_of_type. Use after search_types/search_symbols to inspect a type's members.")]
    public static string ListMembers(
        string typeId,
        string? kind = null,
        string? accessibility = null,
        bool? isStatic = null,
        bool includeInherited = false,
        int limit = 100,
        string? cursor = null,
        string mode = "signatures",
        string? contextAlias = null)
    {
        return GetMembersOfTypeTool.GetMembersOfType(
            typeId,
            kind,
            accessibility,
            isStatic,
            includeInherited,
            limit,
            cursor,
            mode,
            contextAlias);
    }
}

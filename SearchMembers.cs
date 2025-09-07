using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class SearchMembersTool
{
	[McpServerTool, Description("Search members (methods, ctors, properties, fields, events) with rich filters.")]
	public static string SearchMembers(string query, bool regex = false, string? namespaceFilter = null, string? declaringTypeFilter = null, string? attributeFilter = null, string? returnTypeFilter = null, string[]? paramTypeFilters = null, string? kind = null, string? accessibility = null, bool? isStatic = null, bool? isAbstract = null, bool? isVirtual = null, int? genericArity = null, int limit = 50, string? cursor = null)
	{
		/*
		Behavior:
		- Search by name with substring or regex.
		- Apply filters: namespace, declaring type, attribute presence (by full name), return type match, parameter type contains-all, kind, accessibility, static/abstract/virtual, generic arity.
		- Return SearchResult<MemberSummary>. Signature shows short C# signature.
		- Include a small doc summary if XML doc is available.
		*/
		return "TODO";
	}
}

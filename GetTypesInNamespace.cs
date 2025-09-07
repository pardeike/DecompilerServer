using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetTypesInNamespaceTool
{
	[McpServerTool, Description("Get all types inside a namespace. Optional deep traversal for child namespaces.")]
	public static string GetTypesInNamespace(string ns, bool deep = false, int limit = 100, string? cursor = null)
	{
		/*
		Behavior:
		- Enumerate types whose Namespace equals ns (or startsWith(ns + ".") if deep).
		- Return SearchResult<MemberSummary> for types.
		*/
		return "TODO";
	}
}

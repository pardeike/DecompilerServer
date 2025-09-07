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

		Helper methods to use:
		- AssemblyContextManager.IsLoaded to check if assembly is loaded
		- AssemblyContextManager.GetAllTypes() to get all types
		- SearchServiceBase.ApplyPagination() for cursor-based pagination
		- Filter types by namespace (exact match or prefix match if deep=true)
		- ResponseFormatter.TryExecute() for error handling
		- ResponseFormatter.SearchResult() for paginated response formatting
		*/
		return "TODO";
	}
}

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class ListNamespacesTool
{
	[McpServerTool, Description("List namespaces. Optional prefix filter and pagination.")]
	public static string ListNamespaces(string? prefix = null, int limit = 100, string? cursor = null)
	{
		/*
		Inputs: prefix (optional), limit, cursor.

		Behavior:
		- Enumerate distinct namespaces from type system.
		- Prefix match if provided (case-insensitive).
		- Return SearchResult<MemberHandle> where Kind="Namespace" and Name="<ns>".
		*/
		return "TODO";
	}
}
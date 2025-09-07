using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetAstOutlineTool
{
	[McpServerTool, Description("AST outline: lightweight tree summary for a member for quick orientation.")]
	public static string GetAstOutline(string memberId, int maxDepth = 2)
	{
		/*
		Behavior:
		- Use CSharpDecompiler to get SyntaxTree, then extract a compact outline up to maxDepth: nodes with kind, short text, child counts, and line spans.
		- Output a small JSON tree suitable for quick LLM reasoning.
		*/
		return "TODO";
	}
}

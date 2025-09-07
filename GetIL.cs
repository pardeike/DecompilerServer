using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetILTool
{
	[McpServerTool, Description("Get IL for a method or constructor. Format: IL or ILAst.")]
	public static string GetIL(string memberId, string format = "IL")
	{
		/*
		Behavior:
		- Resolve method/ctor. If format=="IL", use Decompiler.Disassembler to dump method body with tokens.
		- If "ILAst", produce high-level IL (if available in library version) or return error if unsupported.
		- Output: { memberId, format, text, totalLines } and page with GetSourceSlice-like slicing if too large.
		*/
		return "TODO";
	}
}

using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class GetDecompiledSourceTool
{
    [McpServerTool, Description("Decompile a member to C#. Caches document and returns document metadata.")]
    public static string GetDecompiledSource(string memberId, bool includeHeader = true)
    {
        /*
		Behavior:
		- Resolve member. Decompile to C# with current settings.
		- Store in cache with line index. Compute stable hash.
		- Return SourceDocument { memberId, language: "C#", totalLines, hash, header? }.
		- Do not return the full code here. Use GetSourceSlice for text.

		Helper methods to use:
		- MemberResolver.ResolveMember() to validate member ID
		- DecompilerService.DecompileMember() for decompilation and caching
		- ResponseFormatter.TryExecute() for error handling
		- ResponseFormatter.SourceDocument() for response formatting
		*/
        return "TODO";
    }
}

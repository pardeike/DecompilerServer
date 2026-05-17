using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

[McpServerToolType]
public static class GetSourceSliceTool
{
    [McpServerTool, Description("Return a line range of source for a resolved memberId. If a human-entered guess is wrong, use returned candidates/hints before shell fallback.")]
    public static string GetSourceSlice(string memberId, int startLine, int endLine, bool includeLineNumbers = false, int context = 0, string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var session = ToolSessionRouter.GetForMember(memberId, contextAlias);
            var contextManager = session.ContextManager;
            var decompilerService = session.DecompilerService;
            var memberResolver = session.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var member = ToolValidation.ResolveMemberOrThrow(session, memberId);

            if (context < 0)
            {
                throw new ArgumentException("context must be non-negative", nameof(context));
            }

            // Apply context to expand the range
            var expandedStartLine = Math.Max(1, startLine - context);
            var expandedEndLine = endLine + context; // Will be capped by the service

            var includeHeader = member is ITypeDefinition;
            var slice = decompilerService.GetSourceSlice(memberId, expandedStartLine, expandedEndLine, includeHeader);

            // Apply line numbers if requested
            if (includeLineNumbers)
            {
                var lines = slice.Code.Split('\n');
                var numberedLines = lines.Select((line, index) =>
                    $"{(slice.StartLine + index).ToString().PadLeft(4)}: {line}");

                var numberedCode = string.Join("\n", numberedLines);

                // Create a new slice with the numbered code
                slice = slice with { Code = numberedCode };
            }

            return slice;
        });
    }
}

using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class GetSourceSliceTool
{
    [McpServerTool, Description("Return a line range of the decompiled source for a member.")]
    public static string GetSourceSlice(string memberId, int startLine, int endLine, bool includeLineNumbers = false, int context = 0)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var decompilerService = ServiceLocator.DecompilerService;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Apply context to expand the range
            var expandedStartLine = Math.Max(1, startLine - context);
            var expandedEndLine = endLine + context; // Will be capped by the service

            var slice = decompilerService.GetSourceSlice(memberId, expandedStartLine, expandedEndLine);

            // Apply line numbers if requested
            if (includeLineNumbers)
            {
                var lines = slice.Code.Split('\n');
                var numberedLines = lines.Select((line, index) =>
                    $"{(slice.StartLine + index).ToString().PadLeft(4)}: {line}");

                var numberedCode = string.Join("\n", numberedLines);

                // Create a new slice with the numbered code
                slice = new SourceSlice
                {
                    MemberId = slice.MemberId,
                    Language = slice.Language,
                    StartLine = slice.StartLine,
                    EndLine = slice.EndLine,
                    TotalLines = slice.TotalLines,
                    Hash = slice.Hash,
                    Code = numberedCode
                };
            }

            return slice;
        });
    }
}

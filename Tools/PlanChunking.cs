using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

public static class PlanChunkingTool
{
    [McpServerTool, Description("Plan line-range chunks for a member's source for LLM-friendly paging.")]
    public static string PlanChunking(string memberId, int targetChunkSize = 6000, int overlap = 2)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;
            var decompilerService = ServiceLocator.DecompilerService;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var member = memberResolver.ResolveMember(memberId);
            if (member == null)
            {
                throw new ArgumentException($"Invalid member ID: {memberId}");
            }

            // Get the source document to analyze
            var document = decompilerService.DecompileMember(memberId, includeHeader: true);
            var totalLines = document.TotalLines;
            
            object result;
            
            if (totalLines == 0)
            {
                result = new
                {
                    memberId = memberId,
                    chunks = new object[0],
                    totalLines = 0,
                    estimatedChars = 0
                };
            }
            else
            {
                // Estimate average characters per line based on the document
                var sampleSlice = decompilerService.GetSourceSlice(memberId, 1, Math.Min(totalLines, 10));
                var avgCharsPerLine = sampleSlice.Code.Length / Math.Min(totalLines, 10);
                
                // If we can't get a good estimate, use a reasonable default
                if (avgCharsPerLine == 0)
                {
                    avgCharsPerLine = 80; // Default assumption for code
                }

                // Calculate target lines per chunk
                var targetLinesPerChunk = Math.Max(1, targetChunkSize / avgCharsPerLine);
                
                var chunks = new List<object>();
                var currentStart = 1;

                while (currentStart <= totalLines)
                {
                    var currentEnd = Math.Min(currentStart + targetLinesPerChunk - 1, totalLines);
                    
                    chunks.Add(new
                    {
                        startLine = currentStart,
                        endLine = currentEnd,
                        estimatedChars = (currentEnd - currentStart + 1) * avgCharsPerLine
                    });

                    // Move to next chunk with overlap consideration
                    currentStart = currentEnd + 1 - overlap;
                    
                    // Prevent infinite loop if overlap is too large
                    if (currentStart <= currentEnd - targetLinesPerChunk + overlap)
                    {
                        currentStart = currentEnd + 1;
                    }
                }

                result = new
                {
                    memberId = memberId,
                    chunks = chunks,
                    totalLines = totalLines,
                    estimatedChars = totalLines * avgCharsPerLine,
                    targetChunkSize = targetChunkSize,
                    overlap = overlap,
                    avgCharsPerLine = avgCharsPerLine
                };
            }

            return result;
        });
    }
}

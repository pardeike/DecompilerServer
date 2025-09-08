using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
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

            if (targetChunkSize <= 0)
            {
                throw new ArgumentException("targetChunkSize must be positive", nameof(targetChunkSize));
            }

            if (overlap <= 0)
            {
                throw new ArgumentException("overlap must be positive", nameof(overlap));
            }

            // Get the source document to analyze
            var document = decompilerService.DecompileMember(memberId, includeHeader: true);
            var totalLines = document.TotalLines;

            ChunkPlanResult result;

            if (totalLines == 0)
            {
                result = new ChunkPlanResult(
                    memberId,
                    Array.Empty<ChunkInfo>(),
                    0,
                    0,
                    targetChunkSize,
                    overlap,
                    0);
            }
            else
            {
                // Estimate average characters per line based on the document
                var divisor = Math.Max(1, Math.Min(totalLines, 10));
                int avgCharsPerLine;
                try
                {
                    var sampleSlice = decompilerService.GetSourceSlice(memberId, 1, divisor);
                    avgCharsPerLine = sampleSlice.Code.Length / divisor;
                }
                catch
                {
                    avgCharsPerLine = 0;
                }

                if (avgCharsPerLine == 0)
                {
                    avgCharsPerLine = 80; // Default assumption for code
                }

                // Calculate target lines per chunk
                var targetLinesPerChunk = Math.Max(1, targetChunkSize / avgCharsPerLine);

                if (overlap >= targetLinesPerChunk)
                {
                    throw new ArgumentException(
                        $"Overlap must be less than {targetLinesPerChunk}",
                        nameof(overlap));
                }

                var chunks = new List<ChunkInfo>();
                var currentStart = 1;

                while (currentStart <= totalLines)
                {
                    var currentEnd = Math.Min(currentStart + targetLinesPerChunk - 1, totalLines);

                    chunks.Add(new ChunkInfo(
                        currentStart,
                        currentEnd,
                        (currentEnd - currentStart + 1) * avgCharsPerLine));

                    // Move to next chunk with overlap consideration
                    var nextStart = currentEnd + 1 - overlap;

                    // Prevent infinite loop by ensuring we always advance
                    if (nextStart <= currentStart)
                    {
                        nextStart = currentStart + 1;
                    }

                    currentStart = nextStart;
                }

                result = new ChunkPlanResult(
                    memberId,
                    chunks.ToArray(),
                    totalLines,
                    totalLines * avgCharsPerLine,
                    targetChunkSize,
                    overlap,
                    avgCharsPerLine);
            }

            return result;
        });
    }
}

internal record ChunkInfo(int StartLine, int EndLine, int EstimatedChars);

internal record ChunkPlanResult(
    string MemberId,
    ChunkInfo[] Chunks,
    int TotalLines,
    int EstimatedChars,
    int TargetChunkSize,
    int Overlap,
    int AvgCharsPerLine);

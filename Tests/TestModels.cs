namespace Tests;

/// <summary>
/// Shared test models for tool implementation tests
/// </summary>
internal record ChunkInfo(int StartLine, int EndLine, int EstimatedChars);

internal record ChunkPlanResult(
    string MemberId,
    ChunkInfo[] Chunks,
    int TotalLines,
    int EstimatedChars,
    int TargetChunkSize,
    int Overlap,
    int AvgCharsPerLine);
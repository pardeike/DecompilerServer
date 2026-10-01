using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class BatchGetDecompiledSourceTool
{
    [McpServerTool(ReadOnly = true), Description("Fetch source with character and per-member line caps (default 50). Partial entries have hasMore/nextStartLine; budgetTruncated means the character cap was exhausted.")]
    public static string BatchGetDecompiledSource(string[] memberIds, int maxTotalChars = 200_000, string? contextAlias = null, int maxLinesPerMember = 50)
    {
        return ResponseFormatter.TryExecute<object>(() =>
        {
            if (memberIds == null || memberIds.Length == 0)
            {
                throw new ArgumentException("Member IDs array cannot be null or empty");
            }

            if (maxTotalChars <= 0 || maxLinesPerMember <= 0)
                throw new ArgumentException("maxTotalChars and maxLinesPerMember must be positive.");

            var results = new List<object>();
            var totalChars = 0;
            var truncated = false;
            var budgetTruncated = false;

            {
                foreach (var memberId in memberIds)
                {
                    try
                    {
                        using var session = ToolSessionRouter.GetForMember(memberId, contextAlias);
                        var contextManager = session.ContextManager;
                        var decompilerService = session.DecompilerService;
                        var memberResolver = session.MemberResolver;

                        if (!contextManager.IsLoaded)
                        {
                            throw new InvalidOperationException("No assembly loaded");
                        }

                        var member = ToolValidation.ResolveMemberOrThrow(session, memberId);
                        var includeHeader = member is ICSharpCode.Decompiler.TypeSystem.ITypeDefinition;
                        var document = decompilerService.DecompileMember(memberId, includeHeader);

                        // Create first slice (typically the whole document or a reasonable portion)
                        var firstSlice = decompilerService.GetSourceSlice(memberId, 1, Math.Min(document.TotalLines, maxLinesPerMember), includeHeader);
                        var sliceLength = firstSlice.Code.Length;

                        // Check if adding this would exceed the limit
                        if (totalChars + sliceLength > maxTotalChars)
                        {
                            truncated = true;
                            budgetTruncated = true;
                            break;
                        }

                        totalChars += sliceLength;
                        var hasMore = firstSlice.EndLine < document.TotalLines;
                        truncated |= hasMore;

                        if (ResponseFormatter.IsCompact)
                        {
                            results.Add(new
                            {
                                memberId = document.MemberId,
                                language = document.Language == "C#" ? null : document.Language,
                                totalLines = document.TotalLines,
                                startLine = firstSlice.StartLine,
                                endLine = firstSlice.EndLine,
                                hasMore,
                                nextStartLine = hasMore ? firstSlice.EndLine + 1 : (int?)null,
                                code = firstSlice.Code,
                                sourceKind = document.SourceKind,
                                sourcePath = document.SourcePath,
                                sourceUri = document.SourceUri
                            });
                            continue;
                        }

                        results.Add(new
                        {
                            doc = new
                            {
                                memberId = document.MemberId,
                                language = document.Language,
                                totalLines = document.TotalLines,
                                hash = document.Hash,
                                includeHeader = document.IncludeHeader
                            },
                            firstSlice = new
                            {
                                memberId = firstSlice.MemberId,
                                language = firstSlice.Language,
                                startLine = firstSlice.StartLine,
                                endLine = firstSlice.EndLine,
                                hasMore,
                                nextStartLine = hasMore ? firstSlice.EndLine + 1 : (int?)null,
                                totalLines = firstSlice.TotalLines,
                                hash = firstSlice.Hash,
                                code = firstSlice.Code
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        if (ResponseFormatter.IsCompact)
                        {
                            results.Add(new
                            {
                                memberId,
                                error = new
                                {
                                    code = ex is ToolErrorException toolError ? toolError.Code : "decompilation_failed",
                                    message = ex.Message,
                                    details = (ex as ToolErrorException)?.DetailsPayload,
                                    hints = (ex as ToolErrorException)?.Hints
                                }
                            });
                            continue;
                        }
                        // Add error entry for failed decompilation
                        results.Add(new
                        {
                            doc = new
                            {
                                memberId = memberId,
                                language = "C#",
                                totalLines = 1,
                                hash = "error",
                                includeHeader = true
                            },
                            firstSlice = new
                            {
                                memberId = memberId,
                                language = "C#",
                                startLine = 1,
                                endLine = 1,
                                totalLines = 1,
                                hash = "error",
                                code = $"// Error decompiling {memberId}: {ex.Message}"
                            }
                        });
                    }
                }

                if (ResponseFormatter.IsCompact)
                    return new { items = results, totalCharacters = totalChars, truncated, budgetTruncated };

                return new
                {
                    items = results,
                    totalCharacters = totalChars,
                    truncated = truncated,
                    budgetTruncated,
                    processed = results.Count,
                    requested = memberIds.Length
                };
            }
        });
    }
}

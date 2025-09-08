using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class BatchGetDecompiledSourceTool
{
    [McpServerTool, Description("Fetch multiple members' decompiled source in one call with size caps.")]
    public static string BatchGetDecompiledSource(string[] memberIds, int maxTotalChars = 200_000)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var decompilerService = ServiceLocator.DecompilerService;
            var memberResolver = ServiceLocator.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            if (memberIds == null || memberIds.Length == 0)
            {
                throw new ArgumentException("Member IDs array cannot be null or empty");
            }

            // Validate all member IDs first
            foreach (var memberId in memberIds)
            {
                var member = memberResolver.ResolveMember(memberId);
                if (member == null)
                {
                    throw new ArgumentException($"Invalid member ID: {memberId}");
                }
            }

            var results = new List<object>();
            var totalChars = 0;
            var truncated = false;

            foreach (var memberId in memberIds)
            {
                try
                {
                    var document = decompilerService.DecompileMember(memberId, includeHeader: true);

                    // Create first slice (typically the whole document or a reasonable portion)
                    var firstSlice = decompilerService.GetSourceSlice(memberId, 1, Math.Min(document.TotalLines, 50));
                    var sliceLength = firstSlice.Code.Length;

                    // Check if adding this would exceed the limit
                    if (totalChars + sliceLength > maxTotalChars)
                    {
                        truncated = true;
                        break;
                    }

                    totalChars += sliceLength;

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
                            totalLines = firstSlice.TotalLines,
                            hash = firstSlice.Hash,
                            code = firstSlice.Code
                        }
                    });
                }
                catch (Exception ex)
                {
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

            return new
            {
                items = results,
                totalCharacters = totalChars,
                truncated = truncated,
                processed = results.Count,
                requested = memberIds.Length
            };
        });
    }
}

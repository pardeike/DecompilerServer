using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using System.Text;
using System.Reflection.Metadata.Ecma335;

namespace DecompilerServer;

[McpServerToolType]
public static class GetILTool
{
    [McpServerTool, Description("Get real IL instructions for a method or constructor. Supports limit/cursor paging and startOffset/endOffset windows. Only format 'IL' is supported.")]
    public static string GetIL(
        string memberId,
        string format = "IL",
        string? contextAlias = null,
        int limit = 500,
        string? cursor = null,
        int? startOffset = null,
        int? endOffset = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var session = ToolSessionRouter.GetForMember(memberId, contextAlias);
            var contextManager = session.ContextManager;
            var memberResolver = session.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Only support IL format for now, ILAst would require additional implementation
            if (!string.Equals(format, "IL", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"Format '{format}' is not supported. Only 'IL' format is currently supported.");
            }

            var method = ToolValidation.ResolveMethodOrThrow(session, memberId);

            var body = IlAnalysisService.ReadMethodBody(method, contextManager);
            var page = PageInstructions(body.Instructions, limit, cursor, startOffset, endOffset);
            var ilText = GenerateILDisassembly(method, body, page.Items, page);

            var result = new
            {
                memberId = memberId,
                format = "IL",
                text = ilText,
                totalLines = ilText.Split('\n').Length,
                isFullDisassembly = body.HasBody && page.IsFullBody,
                noBodyReason = body.NoBodyReason,
                hasMore = page.HasMore,
                nextCursor = page.NextCursor,
                totalInstructionCount = body.Instructions.Count,
                returnedInstructionCount = page.Items.Count,
                instructionStartIndex = page.Items.Count > 0 ? page.StartIndex : (int?)null,
                instructionEndIndex = page.Items.Count > 0 ? page.StartIndex + page.Items.Count - 1 : (int?)null,
                offsetWindow = startOffset.HasValue || endOffset.HasValue ? new
                {
                    startOffset,
                    endOffset
                } : null,
                body = body.HasBody ? new
                {
                    body.RelativeVirtualAddress,
                    body.MaxStack,
                    body.LocalVariablesInitialized,
                    body.LocalSignatureToken,
                    body.CodeSize,
                    instructionCount = body.Instructions.Count
                } : null,
                instructions = page.Items
            };

            return result;
        });
    }

    private static string GenerateILDisassembly(
        ICSharpCode.Decompiler.TypeSystem.IMethod method,
        MethodIlBody body,
        IReadOnlyList<IlInstructionInfo> instructions,
        IlInstructionPage page)
    {
        var output = new StringBuilder();
        output.AppendLine($"// Method: {method.FullName}");
        output.AppendLine($"// Metadata Token: 0x{MetadataTokens.GetToken(method.MetadataToken):X8}");
        output.AppendLine($"// Has Body: {body.HasBody}");

        if (!body.HasBody)
        {
            output.AppendLine($"// No Body Reason: {body.NoBodyReason}");
            output.AppendLine();
            output.Append(GenerateMethodSummary(method));
            return output.ToString();
        }

        output.AppendLine($"// RVA: 0x{body.RelativeVirtualAddress!.Value:X}");
        output.AppendLine($"// Max Stack: {body.MaxStack}");
        output.AppendLine($"// Code Size: {body.CodeSize}");
        output.AppendLine($"// Instructions: {instructions.Count} of {page.FilteredCount}");
        if (!page.IsFullBody)
        {
            output.AppendLine($"// Window Start Index: {page.StartIndex}");
            output.AppendLine($"// Has More: {page.HasMore}");
            if (page.NextCursor != null)
                output.AppendLine($"// Next Cursor: {page.NextCursor}");
        }
        output.AppendLine();

        foreach (var instruction in instructions)
        {
            output.AppendLine(IlAnalysisService.FormatInstruction(instruction));
        }

        return output.ToString();
    }

    private static string GenerateMethodSummary(ICSharpCode.Decompiler.TypeSystem.IMethod method)
    {
        var summary = $"// Method: {method.FullName}\n";
        summary += $"// Declaring Type: {method.DeclaringType?.FullName}\n";
        summary += $"// Return Type: {method.ReturnType.FullName}\n";
        summary += $"// Parameters: {method.Parameters.Count}\n";

        foreach (var param in method.Parameters)
        {
            summary += $"//   {param.Type.FullName} {param.Name}\n";
        }

        summary += $"// Is Constructor: {method.IsConstructor}\n";
        summary += $"// Is Static: {method.IsStatic}\n";
        summary += $"// Is Virtual: {method.IsVirtual}\n";
        summary += $"// Is Abstract: {method.IsAbstract}\n";
        summary += $"// Accessibility: {method.Accessibility}\n";
        summary += $"// Metadata Token: 0x{MetadataTokens.GetToken(method.MetadataToken):X8}\n";

        return summary;
    }

    private static IlInstructionPage PageInstructions(
        IReadOnlyList<IlInstructionInfo> instructions,
        int limit,
        string? cursor,
        int? startOffset,
        int? endOffset)
    {
        var filtered = instructions
            .Where(instruction => !startOffset.HasValue || instruction.Offset >= startOffset.Value)
            .Where(instruction => !endOffset.HasValue || instruction.Offset <= endOffset.Value)
            .ToList();

        var normalizedLimit = limit <= 0 ? 500 : Math.Min(limit, 1000);
        var startIndex = ParseCursor(cursor);

        var pageItems = filtered.Skip(startIndex).Take(normalizedLimit).ToList();
        var hasMore = startIndex + normalizedLimit < filtered.Count;
        var isFullBody = !startOffset.HasValue
            && !endOffset.HasValue
            && startIndex == 0
            && !hasMore
            && pageItems.Count == instructions.Count;

        return new IlInstructionPage(
            pageItems,
            startIndex,
            filtered.Count,
            hasMore,
            hasMore ? (startIndex + normalizedLimit).ToString() : null,
            isFullBody);
    }

    private static int ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;

        if (!int.TryParse(cursor, out var startIndex) || startIndex < 0)
            throw new ArgumentException("cursor must be a non-negative integer.", nameof(cursor));

        return startIndex;
    }

    private sealed record IlInstructionPage(
        List<IlInstructionInfo> Items,
        int StartIndex,
        int FilteredCount,
        bool HasMore,
        string? NextCursor,
        bool IsFullBody);
}

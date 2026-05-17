using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using System.Text;

namespace DecompilerServer;

[McpServerToolType]
public static class GetILTool
{
    [McpServerTool, Description("Get real IL instructions for a method or constructor. Only format 'IL' is supported; methods without bodies report no_il_body.")]
    public static string GetIL(string memberId, string format = "IL", string? contextAlias = null)
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
            if (format != "IL")
            {
                throw new NotSupportedException($"Format '{format}' is not supported. Only 'IL' format is currently supported.");
            }

            var method = ToolValidation.ResolveMethodOrThrow(session, memberId);

            var body = IlAnalysisService.ReadMethodBody(method, contextManager);
            var ilText = GenerateILDisassembly(method, body);

            var result = new
            {
                memberId = memberId,
                format = format,
                text = ilText,
                totalLines = ilText.Split('\n').Length,
                isFullDisassembly = body.HasBody,
                noBodyReason = body.NoBodyReason,
                body = body.HasBody ? new
                {
                    body.RelativeVirtualAddress,
                    body.MaxStack,
                    body.LocalVariablesInitialized,
                    body.LocalSignatureToken,
                    body.CodeSize,
                    instructionCount = body.Instructions.Count
                } : null,
                instructions = body.Instructions
            };

            return result;
        });
    }

    private static string GenerateILDisassembly(ICSharpCode.Decompiler.TypeSystem.IMethod method, MethodIlBody body)
    {
        var output = new StringBuilder();
        output.AppendLine($"// Method: {method.FullName}");
        output.AppendLine($"// Metadata Token: {method.MetadataToken:X8}");
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
        output.AppendLine();

        foreach (var instruction in body.Instructions)
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
        summary += $"// Metadata Token: {method.MetadataToken:X8}\n";

        return summary;
    }
}

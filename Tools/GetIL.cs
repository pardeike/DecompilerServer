using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using System.Text;
using System.Reflection.Metadata;

namespace DecompilerServer;

public static class GetILTool
{
    [McpServerTool, Description("Get IL for a method or constructor. Format: IL or ILAst.")]
    public static string GetIL(string memberId, string format = "IL")
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Only support IL format for now, ILAst would require additional implementation
            if (format != "IL")
            {
                throw new NotSupportedException($"Format '{format}' is not supported. Only 'IL' format is currently supported.");
            }

            var entity = memberResolver.ResolveMember(memberId);
            if (entity is not IMethod method)
            {
                throw new ArgumentException($"Member ID '{memberId}' could not be resolved to a method or constructor");
            }

            var ilText = GenerateILDisassembly(method, contextManager);

            var result = new
            {
                memberId = memberId,
                format = format,
                text = ilText,
                totalLines = ilText.Split('\n').Length
            };

            return result;
        });
    }

    private static string GenerateILDisassembly(IMethod method, AssemblyContextManager contextManager)
    {
        try
        {
            var peFile = contextManager.GetPEFile();

            if (peFile == null)
            {
                return GenerateMethodSummary(method);
            }

            var metadataReader = peFile.Metadata;
            var methodHandle = method.MetadataToken;

            if (methodHandle.IsNil)
            {
                return GenerateMethodSummary(method) + "\n\n// No metadata token available for IL disassembly";
            }

            var output = new StringBuilder();

            // Add method header information
            output.AppendLine($"// Method: {method.FullName}");
            output.AppendLine($"// Token: {methodHandle:X8}");
            output.AppendLine();

            try
            {
                // Try to get method definition from handle
                if (methodHandle.Kind == HandleKind.MethodDefinition)
                {
                    var methodDef = (MethodDefinitionHandle)methodHandle;
                    var methodDefinition = metadataReader.GetMethodDefinition(methodDef);

                    output.AppendLine($"// RVA: 0x{methodDefinition.RelativeVirtualAddress:X}");
                    output.AppendLine($"// Implementation Flags: {methodDefinition.ImplAttributes}");
                    output.AppendLine($"// Attributes: {methodDefinition.Attributes}");

                    // Basic method body information
                    if (methodDefinition.RelativeVirtualAddress != 0)
                    {
                        output.AppendLine("// Method has IL body");
                        output.AppendLine("// Note: Full IL disassembly would require additional implementation");
                        output.AppendLine("// This is a simplified version showing method metadata");
                    }
                    else
                    {
                        output.AppendLine("// Method has no IL body (abstract, extern, or interface method)");
                    }
                }
                else
                {
                    output.AppendLine("// Method handle is not a method definition");
                }
            }
            catch (Exception ex)
            {
                output.AppendLine($"// Error during IL analysis: {ex.Message}");
            }

            output.AppendLine();
            output.AppendLine(GenerateMethodSummary(method));
            return output.ToString();
        }
        catch (Exception ex)
        {
            return GenerateMethodSummary(method) + $"\n\n// Error generating IL: {ex.Message}";
        }
    }

    private static string GenerateMethodSummary(IMethod method)
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

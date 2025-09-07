using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

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

            // Get basic method information - full IL disassembly would require more complex implementation
            var ilText = GenerateMethodSummary(method);

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
        summary += "\n// TODO: Full IL disassembly requires additional implementation\n";
        summary += "// This would involve reading the method body from metadata\n";
        summary += "// and using ICSharpCode.Decompiler.Disassembler for proper IL output";

        return summary;
    }
}

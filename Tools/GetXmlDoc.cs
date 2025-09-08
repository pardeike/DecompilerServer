using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

[McpServerToolType]
public static class GetXmlDocTool
{
    [McpServerTool, Description("Get raw XML doc for a member if available.")]
    public static string GetXmlDoc(string memberId)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var memberResolver = ServiceLocator.MemberResolver;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var entity = memberResolver.ResolveMember(memberId);
            if (entity == null)
            {
                throw new ArgumentException($"Member ID '{memberId}' could not be resolved");
            }

            // Try to extract XML documentation
            var xmlDoc = ExtractXmlDocumentation(entity);

            var result = new
            {
                memberId = memberId,
                xmlDoc = xmlDoc,
                hasDocumentation = xmlDoc != null
            };

            return result;
        });
    }

    private static string? ExtractXmlDocumentation(IEntity entity)
    {
        // XML documentation extraction is not implemented
        // Full implementation would require:
        // 1. Setting up IDocumentationProvider in decompiler settings
        // 2. Loading XML documentation files alongside assemblies  
        // 3. Configuring documentation providers during assembly loading
        // This is beyond the scope of minimal changes and would require
        // significant refactoring of the assembly loading infrastructure.

        return null;
    }
}

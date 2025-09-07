using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer;

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
        // TODO: Implement XML documentation extraction
        //
        // Basic XML documentation extraction
        // The decompiler system has XML documentation support but accessing it requires
        // more complex setup with XML documentation providers. For now, we return null
        // as implementing full XML documentation parsing would require significant additional code.
        // 
        // In a full implementation, you would:
        // 1. Set up IDocumentationProvider in the decompiler settings
        // 2. Load XML documentation files alongside assemblies
        // 3. Use entity.GetDocumentation() or similar methods

        return null;
    }
}

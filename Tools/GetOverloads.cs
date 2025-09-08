using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;

namespace DecompilerServer;

[McpServerToolType]
public static class GetOverloadsTool
{
    [McpServerTool, Description("Find overloads for a method name within its declaring type.")]
    public static string GetOverloads(string memberId)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var inheritanceAnalyzer = ServiceLocator.InheritanceAnalyzer;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var overloads = inheritanceAnalyzer.GetOverloads(memberId);
            var overloadList = overloads
                .OrderBy(m => GetParameterCount(m.Signature))
                .ThenBy(m => m.Signature)
                .ToList();

            var result = new SearchResult<MemberSummary>(overloadList, false, null, overloadList.Count);

            return result;
        });
    }

    private static int GetParameterCount(string signature)
    {
        // Simple parameter count extraction from signature
        // This counts commas in parameter list plus 1, handling empty parameter lists
        var paramStart = signature.IndexOf('(');
        var paramEnd = signature.IndexOf(')');

        if (paramStart == -1 || paramEnd == -1 || paramEnd <= paramStart)
            return 0;

        var paramSection = signature.Substring(paramStart + 1, paramEnd - paramStart - 1).Trim();
        if (string.IsNullOrEmpty(paramSection))
            return 0;

        return paramSection.Split(',').Length;
    }
}

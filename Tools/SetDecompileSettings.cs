using System.ComponentModel;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler;

namespace DecompilerServer;

[McpServerToolType]
public static class SetDecompileSettingsTool
{
    [McpServerTool, Description("Update decompiler settings (e.g., UsingDeclarations, ShowXmlDocumentation).")]
    public static string SetDecompileSettings(Dictionary<string, object> settings)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var contextManager = ServiceLocator.ContextManager;
            var decompilerService = ServiceLocator.DecompilerService;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            // Get current settings
            var currentSettings = new DecompilerSettings
            {
                UsingDeclarations = true,
                ShowXmlDocumentation = true,
                NamedArguments = true,
                MakeAssignmentExpressions = true,
                AlwaysUseBraces = true,
                RemoveDeadCode = true,
                IntroduceIncrementAndDecrement = true
            };

            // Apply new settings
            var updatedSettings = ApplySettingsChanges(currentSettings, settings);

            // Update the decompiler with new settings
            contextManager.UpdateSettings(updatedSettings);

            // Clear decompiled source cache since output may change
            decompilerService.ClearCache();

            // Return current effective settings
            var effectiveSettings = new
            {
                usingDeclarations = updatedSettings.UsingDeclarations,
                showXmlDocumentation = updatedSettings.ShowXmlDocumentation,
                namedArguments = updatedSettings.NamedArguments,
                makeAssignmentExpressions = updatedSettings.MakeAssignmentExpressions,
                alwaysUseBraces = updatedSettings.AlwaysUseBraces,
                removeDeadCode = updatedSettings.RemoveDeadCode,
                introduceIncrementAndDecrement = updatedSettings.IntroduceIncrementAndDecrement
            };

            return effectiveSettings;
        });
    }

    private static DecompilerSettings ApplySettingsChanges(DecompilerSettings currentSettings, Dictionary<string, object> changes)
    {
        var newSettings = new DecompilerSettings
        {
            UsingDeclarations = currentSettings.UsingDeclarations,
            ShowXmlDocumentation = currentSettings.ShowXmlDocumentation,
            NamedArguments = currentSettings.NamedArguments,
            MakeAssignmentExpressions = currentSettings.MakeAssignmentExpressions,
            AlwaysUseBraces = currentSettings.AlwaysUseBraces,
            RemoveDeadCode = currentSettings.RemoveDeadCode,
            IntroduceIncrementAndDecrement = currentSettings.IntroduceIncrementAndDecrement
        };

        foreach (var (key, value) in changes)
        {
            switch (key.ToLowerInvariant())
            {
                case "usingdeclarations":
                    if (value is bool boolVal1) newSettings.UsingDeclarations = boolVal1;
                    break;
                case "showxmldocumentation":
                    if (value is bool boolVal2) newSettings.ShowXmlDocumentation = boolVal2;
                    break;
                case "namedarguments":
                    if (value is bool boolVal3) newSettings.NamedArguments = boolVal3;
                    break;
                case "makeassignmentexpressions":
                    if (value is bool boolVal4) newSettings.MakeAssignmentExpressions = boolVal4;
                    break;
                case "alwaysuseBraces":
                    if (value is bool boolVal5) newSettings.AlwaysUseBraces = boolVal5;
                    break;
                case "removedeadcode":
                    if (value is bool boolVal6) newSettings.RemoveDeadCode = boolVal6;
                    break;
                case "introduceincrementanddecrement":
                    if (value is bool boolVal7) newSettings.IntroduceIncrementAndDecrement = boolVal7;
                    break;
            }
        }

        return newSettings;
    }
}
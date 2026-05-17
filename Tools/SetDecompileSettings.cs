using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using DecompilerServer.Services;
using ICSharpCode.Decompiler;

namespace DecompilerServer;

[McpServerToolType]
public static class SetDecompileSettingsTool
{
    [McpServerTool, Description("Update decompiler settings (e.g., UsingDeclarations, ShowXmlDocumentation).")]
    public static string SetDecompileSettings(Dictionary<string, object> settings, string? contextAlias = null)
    {
        return ResponseFormatter.TryExecute(() =>
        {
            var session = ToolSessionRouter.GetForContext(contextAlias);
            var contextManager = session.ContextManager;
            var decompilerService = session.DecompilerService;

            if (!contextManager.IsLoaded)
            {
                throw new InvalidOperationException("No assembly loaded");
            }

            var currentSettings = contextManager.GetSettings();

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
            switch (NormalizeSettingKey(key))
            {
                case "usingdeclarations":
                    if (TryReadBoolean(value, out var boolVal1)) newSettings.UsingDeclarations = boolVal1;
                    break;
                case "showxmldocumentation":
                    if (TryReadBoolean(value, out var boolVal2)) newSettings.ShowXmlDocumentation = boolVal2;
                    break;
                case "namedarguments":
                    if (TryReadBoolean(value, out var boolVal3)) newSettings.NamedArguments = boolVal3;
                    break;
                case "makeassignmentexpressions":
                    if (TryReadBoolean(value, out var boolVal4)) newSettings.MakeAssignmentExpressions = boolVal4;
                    break;
                case "alwaysusebraces":
                    if (TryReadBoolean(value, out var boolVal5)) newSettings.AlwaysUseBraces = boolVal5;
                    break;
                case "removedeadcode":
                    if (TryReadBoolean(value, out var boolVal6)) newSettings.RemoveDeadCode = boolVal6;
                    break;
                case "introduceincrementanddecrement":
                    if (TryReadBoolean(value, out var boolVal7)) newSettings.IntroduceIncrementAndDecrement = boolVal7;
                    break;
            }
        }

        return newSettings;
    }

    private static string NormalizeSettingKey(string key)
    {
        return key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static bool TryReadBoolean(object? value, out bool result)
    {
        switch (value)
        {
            case bool boolValue:
                result = boolValue;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True }:
                result = true;
                return true;
            case JsonElement { ValueKind: JsonValueKind.False }:
                result = false;
                return true;
            case JsonElement { ValueKind: JsonValueKind.String } element
                when bool.TryParse(element.GetString(), out var parsed):
                result = parsed;
                return true;
            case string stringValue when bool.TryParse(stringValue, out var parsed):
                result = parsed;
                return true;
            default:
                result = false;
                return false;
        }
    }
}

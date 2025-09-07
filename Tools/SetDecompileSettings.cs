using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public static class SetDecompileSettingsTool
{
	[McpServerTool, Description("Update decompiler settings (e.g., UsingDeclarations, ShowXmlDocumentation).")]
	public static string SetDecompileSettings(Dictionary<string, object> settings)
	{
		/*
		Goal: Apply a subset of CSharpDecompilerSettings at runtime.

		Inputs: settings as string->value. Supported keys:
		- "UsingDeclarations", "ShowXmlDocumentation", "NamedArguments", "MakeAssignmentExpressions", "AlwaysUseBraces", ...
		Behavior:
		- Validate known keys, ignore unknown, persist to context, clear code cache (since output can change).
		- Return current effective settings.
		*/
		return "TODO";
	}
}
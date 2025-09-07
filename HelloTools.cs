using ModelContextProtocol.Server;
using System.ComponentModel;

namespace DecompilerServer;

[McpServerToolType]
public static class HelloTools
{
	[McpServerTool, Description("Returns a friendly greeting.")]
	public static string Hello([Description("Name to greet")] string name = "world")
		=> $"Hello, {name}!";

	[McpServerTool, Description("Health check that proves we’re alive.")]
	public static string Ping() => "pong";

	[McpServerTool, Description("Reverse a string, because why not.")]
	public static string Reverse(string text) => new([.. (text ?? "").Reverse()]);
}
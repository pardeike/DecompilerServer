using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);

// Important: send logs to STDERR so we don't corrupt MCP JSON on STDOUT
builder.Logging.AddConsole(o =>
{
	o.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
	.AddMcpServer()                 // core MCP server services
	.WithStdioServerTransport()     // Codex talks to STDIO servers
	.WithToolsFromAssembly();       // auto-discover [McpServerTool]s in this assembly

await builder.Build().RunAsync();

// ---- Tools (functions callable by the client/LLM) ----
[McpServerToolType]
public static class HelloTools
{
	[McpServerTool, Description("Returns a friendly greeting.")]
	public static string Hello([Description("Name to greet")] string name = "world")
		=> $"Hello, {name}!";

	[McpServerTool, Description("Health check that proves we’re alive.")]
	public static string Ping() => "pong";

	[McpServerTool, Description("Reverse a string, because why not.")]
	public static string Reverse(string text) => new string((text ?? "").Reverse().ToArray());
}
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DecompilerServer.Services;

namespace DecompilerServer;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new StderrLoggerProvider());
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        // builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        // builder.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
        builder.Services.AddHostedService<StartupLogService>();

        // Register DecompilerServer services as singletons for state persistence
        builder.Services.AddSingleton<AssemblyContextManager>();
        builder.Services.AddSingleton<MemberResolver>();
        builder.Services.AddSingleton<DecompilerService>();
        builder.Services.AddSingleton<UsageAnalyzer>();
        builder.Services.AddSingleton<InheritanceAnalyzer>();
        builder.Services.AddSingleton<ResponseFormatter>();

        builder.Services
            .AddMcpServer()             // core MCP server services
            .WithStdioServerTransport() // Codex talks to STDIO servers
            .WithToolsFromAssembly();   // auto-discover [McpServerTool]s in this assembly

        var app = builder.Build();

        // Initialize service locator
        ServiceLocator.SetServiceProvider(app.Services);

        await app.RunAsync();
    }
}

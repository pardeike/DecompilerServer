using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DecompilerServer.Services;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public partial class Program
{
    internal const string ServerInstructions = """
        DecompilerServer inspects loaded .NET assemblies. Use search_symbols first when you have a partial, qualified, or guessed name.
        Common parameter names: search_types/search_members use query, not pattern; resolve_member_id/get_decompiled_source/find_usages use memberId; find_callers/find_callees/get_overrides use methodId; get_types_in_namespace uses ns.
        After resolving a type, use list_members or get_members_of_type before guessing method names. If a lookup fails, inspect structured error.details, candidates, and hints before retrying.
        """;

    internal static void ConfigureMcpServerOptions(McpServerOptions options)
    {
        options.ServerInstructions = ServerInstructions;
    }

    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new StderrLoggerProvider());
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        // builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
        // builder.Logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
        builder.Services.AddHostedService<WorkspaceBootstrapService>();

        // Register DecompilerServer services as singletons for state persistence
        builder.Services.AddSingleton<DecompilerWorkspace>();
        builder.Services.AddSingleton<AssemblyContextManager>();
        builder.Services.AddSingleton<MemberResolver>();
        builder.Services.AddSingleton<DecompilerService>();
        builder.Services.AddSingleton<UsageAnalyzer>();
        builder.Services.AddSingleton<InheritanceAnalyzer>();
        builder.Services.AddSingleton<ResponseFormatter>();

        builder.Services
            .AddMcpServer(ConfigureMcpServerOptions) // core MCP server services
            .WithStdioServerTransport() // Codex talks to STDIO servers
            .WithToolsFromAssembly();   // auto-discover [McpServerTool]s in this assembly

        var app = builder.Build();

        // Initialize service locator
        ServiceLocator.SetServiceProvider(app.Services);

        await app.RunAsync();
    }
}

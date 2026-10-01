using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DecompilerServer.Services;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public partial class Program
{
    internal const string ServerInstructions = """
        DecompilerServer inspects loaded .NET assemblies. Use search_symbols first for fragments; use resolve_member_id first for fully-qualified or XML-doc-like guessed symbols.
        Callers/usages default to assembly scope; scope=workspace scans loaded contexts only and reports searchedContexts. Batch entries report hasMore/nextStartLine; budgetTruncated means the character cap was exhausted.
        Common parameter names: search_types/search_members use query, not pattern; resolve_member_id/get_decompiled_source/find_usages use memberId; find_callers/find_callees/get_overrides use methodId; get_types_in_namespace uses ns.
        After resolving a type, use list_members before guessing method names. If a lookup fails, inspect structured error.details, candidates, and hints before retrying.
        Use status for readiness; list_contexts(includeRegistered=true) pages registered aliases; passing contextAlias activates a registered alias on demand. Use get_server_stats for detailed cache/index diagnostics.
        Compact output is the default: complete source uses code; omitted source language means C#. Source slices keep actual line bounds, and IL uses structured instructions. Extended output is a user startup setting, not a tool argument.
        """;

    internal static void ConfigureMcpServerOptions(McpServerOptions options)
    {
        options.ServerInstructions = ServerInstructions;
    }

    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(ResponseFormatOptions.FromEnvironment());
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

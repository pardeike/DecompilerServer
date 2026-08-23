using System.Net;
using DecompilerServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace DecompilerServer;

public partial class Program
{
    internal const string DefaultHttpUrl = "http://127.0.0.1:30503";

    internal const string ServerInstructions = """
        DecompilerServer inspects loaded .NET assemblies. Use search_symbols first for fragments; use resolve_member_id first for fully-qualified or XML-doc-like guessed symbols.
        Common parameter names: search_types/search_members use query, not pattern; resolve_member_id/get_decompiled_source/find_usages use memberId; find_callers/find_callees/get_overrides use methodId; get_types_in_namespace uses ns.
        After resolving a type, use list_members or get_members_of_type before guessing method names. If a lookup fails, inspect structured error.details, candidates, and hints before retrying.
        Use status/list_contexts to confirm loaded and registered aliases; passing contextAlias activates a registered alias on demand. Use get_server_stats for detailed cache/index diagnostics.
        """;

    internal static void ConfigureMcpServerOptions(McpServerOptions options)
    {
        options.ServerInstructions = ServerInstructions;
    }

    public static async Task Main(string[] args)
    {
        var launchOptions = ParseLaunchOptions(
            args,
            Environment.GetEnvironmentVariable("DECOMPILER_TRANSPORT"),
            Environment.GetEnvironmentVariable("DECOMPILER_HTTP_URL"));

        if (launchOptions.UseHttp)
        {
            await RunHttpAsync(launchOptions);
            return;
        }

        await RunStdioAsync(launchOptions.HostArguments);
    }

    internal static LaunchOptions ParseLaunchOptions(
        string[] args,
        string? transportEnvironment,
        string? httpUrlEnvironment)
    {
        var useHttpArgument = args.Any(arg => string.Equals(arg, "--http", StringComparison.OrdinalIgnoreCase));
        var hostArguments = args
            .Where(arg => !string.Equals(arg, "--http", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var useHttp = useHttpArgument || string.Equals(transportEnvironment, "http", StringComparison.OrdinalIgnoreCase);
        var httpUrl = string.IsNullOrWhiteSpace(httpUrlEnvironment) ? DefaultHttpUrl : httpUrlEnvironment.Trim();

        if (useHttp)
            ValidateLoopbackHttpUrl(httpUrl);

        return new LaunchOptions(useHttp, httpUrl, hostArguments);
    }

    internal static void ValidateLoopbackHttpUrl(string httpUrl)
    {
        if (!Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                && (!IPAddress.TryParse(uri.Host, out var address) || !IPAddress.IsLoopback(address)))
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException(
                "DECOMPILER_HTTP_URL must be an HTTP loopback origin such as http://127.0.0.1:30503.",
                nameof(httpUrl));
        }
    }

    private static async Task RunStdioAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureLogging(builder.Logging);
        AddDecompilerServices(builder.Services);
        builder.Services
            .AddMcpServer(ConfigureMcpServerOptions)
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        var app = builder.Build();
        ServiceLocator.SetServiceProvider(app.Services);
        await app.RunAsync();
    }

    private static async Task RunHttpAsync(LaunchOptions launchOptions)
    {
        var builder = WebApplication.CreateBuilder(launchOptions.HostArguments);
        ConfigureLogging(builder.Logging);
        builder.WebHost.UseUrls(launchOptions.HttpUrl);
        builder.Configuration["AllowedHosts"] = "localhost;127.0.0.1;[::1]";
        AddDecompilerServices(builder.Services);
        builder.Services
            .AddMcpServer(ConfigureMcpServerOptions)
            .WithHttpTransport(options =>
            {
                options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
#pragma warning disable MCP9006
                options.IdleTimeout = TimeSpan.FromMinutes(10);
                options.MaxIdleSessionCount = 64;
#pragma warning restore MCP9006
            })
            .WithToolsFromAssembly();

        var app = builder.Build();
        app.MapMcp("/mcp");
        ServiceLocator.SetServiceProvider(app.Services);
        await app.RunAsync();
    }

    private static void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddProvider(new StderrLoggerProvider());
        logging.SetMinimumLevel(LogLevel.Information);
    }

    private static void AddDecompilerServices(IServiceCollection services)
    {
        services.AddHostedService<WorkspaceBootstrapService>();
        services.AddSingleton<DecompilerWorkspace>();
        services.AddSingleton<AssemblyContextManager>();
        services.AddSingleton<MemberResolver>();
        services.AddSingleton<DecompilerService>();
        services.AddSingleton<UsageAnalyzer>();
        services.AddSingleton<InheritanceAnalyzer>();
        services.AddSingleton<ResponseFormatter>();
    }

    internal sealed record LaunchOptions(bool UseHttp, string HttpUrl, string[] HostArguments);
}

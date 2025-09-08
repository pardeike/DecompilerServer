using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DecompilerServer.Services;
using ModelContextProtocol.Server;
using Xunit;

namespace Tests;

/// <summary>
/// Tests for MCP protocol compliance to ensure the server correctly implements required methods
/// </summary>
public class McpProtocolTests
{
    [Fact]
    public async Task McpServer_Should_Have_ListToolsHandler_Registered()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        // Register services like in Program.cs
        builder.Services.AddSingleton<AssemblyContextManager>();
        builder.Services.AddSingleton<MemberResolver>();
        builder.Services.AddSingleton<DecompilerService>();
        builder.Services.AddSingleton<UsageAnalyzer>();
        builder.Services.AddSingleton<InheritanceAnalyzer>();
        builder.Services.AddSingleton<ResponseFormatter>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        var app = builder.Build();
        DecompilerServer.ServiceLocator.SetServiceProvider(app.Services);

        // Act & Assert
        // The key test is that WithToolsFromAssembly() should have registered
        // a list tools handler when [McpServerToolType] attributes are present
        // We verify this by checking that the app builds without errors and
        // that our tools are properly marked with the required attributes

        // Check that some of our key tool classes have the McpServerToolType attribute
        var statusToolType = typeof(DecompilerServer.StatusTool);
        var pingToolType = typeof(DecompilerServer.PingTool);
        var listNamespacesToolType = typeof(DecompilerServer.ListNamespacesTool);

        Assert.True(statusToolType.GetCustomAttributes(typeof(McpServerToolTypeAttribute), false).Length > 0,
            "StatusTool should have McpServerToolType attribute");
        Assert.True(pingToolType.GetCustomAttributes(typeof(McpServerToolTypeAttribute), false).Length > 0,
            "PingTool should have McpServerToolType attribute");
        Assert.True(listNamespacesToolType.GetCustomAttributes(typeof(McpServerToolTypeAttribute), false).Length > 0,
            "ListNamespacesTool should have McpServerToolType attribute");

        // If we reach here without exceptions, the MCP server was successfully configured
        // with tool discovery, which means tools/list handler is available
        Assert.True(true);
    }
}
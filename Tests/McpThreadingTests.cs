using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace Tests;

/// <summary>
/// Tests to verify that MCP tools work correctly when called from different threads,
/// simulating the real MCP server environment.
/// </summary>
public class McpThreadingTests : ServiceTestBase
{
    private readonly IServiceProvider _serviceProvider;

    public McpThreadingTests()
    {
        // Set up service provider like in production
        var services = new ServiceCollection();
        services.AddSingleton(ContextManager);
        services.AddSingleton(MemberResolver);
        services.AddSingleton<DecompilerService>();
        services.AddSingleton<UsageAnalyzer>();
        services.AddSingleton<InheritanceAnalyzer>();
        services.AddSingleton<ResponseFormatter>();

        _serviceProvider = services.BuildServiceProvider();

        // Simulate production setup: set global provider once
        ServiceLocator.SetServiceProvider(_serviceProvider);
    }

    [Fact]
    public void McpTools_Should_Work_From_Different_Threads()
    {
        // This test simulates the MCP server scenario where tools are called
        // from worker threads that don't have thread-local service providers

        var results = new List<string>();
        var tasks = new List<Task>();
        var exceptions = new List<Exception>();

        // Create multiple tasks that simulate MCP tool calls from worker threads
        for (int i = 0; i < 5; i++)
        {
            int taskId = i;
            var task = Task.Run(() =>
            {
                try
                {
                    // These calls should work even though this thread doesn't have
                    // a thread-local service provider set
                    var pingResult = PingTool.Ping();
                    var statusResult = StatusTool.Status();
                    var namespacesResult = ListNamespacesTool.ListNamespaces();

                    lock (results)
                    {
                        results.Add($"Task {taskId}: Success");
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            });

            tasks.Add(task);
        }

        // Wait for all tasks to complete
        Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(30));

        // Assert that no exceptions occurred
        Assert.Empty(exceptions);
        Assert.Equal(5, results.Count);

        // Verify that at least one tool call returned valid JSON
        var pingResult = PingTool.Ping();
        Assert.NotNull(pingResult);
        var response = JsonSerializer.Deserialize<JsonElement>(pingResult);
        Assert.Equal("ok", response.GetProperty("status").GetString());
    }

    [Fact]
    public void McpTools_Should_Work_Without_ThreadLocal_ServiceProvider()
    {
        // Explicitly clear thread-local provider to simulate MCP worker thread
        ServiceLocator.SetServiceProvider(null!);

        // The global provider should still allow tools to work
        var result = PingTool.Ping();
        Assert.NotNull(result);

        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        // Restore for other tests
        ServiceLocator.SetServiceProvider(_serviceProvider);
    }
}
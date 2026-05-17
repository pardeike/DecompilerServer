using System.Text.Json;
using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class WorkspaceToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IServiceProvider _serviceProvider;

    public WorkspaceToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"WorkspaceToolTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddSingleton(_ => new DecompilerWorkspace(Path.Combine(_tempDir, "contexts.json")));
        services.AddSingleton<AssemblyContextManager>();
        services.AddSingleton<MemberResolver>();
        services.AddSingleton<DecompilerService>();
        services.AddSingleton<UsageAnalyzer>();
        services.AddSingleton<InheritanceAnalyzer>();
        services.AddSingleton<ResponseFormatter>();

        _serviceProvider = services.BuildServiceProvider();
        ServiceLocator.SetServiceProvider(_serviceProvider);
    }

    [Fact]
    public void LoadAssembly_WithContextAliases_AllowsListingLoadedContexts()
    {
        // Arrange
        var firstAssembly = TestAssemblyLocator.GetPath();
        var secondAssembly = typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location;
        var beforeLoad = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        var firstResult = LoadAssemblyTool.LoadAssembly(assemblyPath: firstAssembly, contextAlias: "rw14", rebuildIndex: false);
        var secondResult = LoadAssemblyTool.LoadAssembly(assemblyPath: secondAssembly, contextAlias: "rw15", rebuildIndex: false, makeCurrent: false);
        var contextsResult = ListContextsTool.ListContexts();
        var afterList = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Assert
        Assert.DoesNotContain("error", firstResult, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error", secondResult, StringComparison.OrdinalIgnoreCase);

        var response = JsonSerializer.Deserialize<JsonElement>(contextsResult);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal("rw14", data.GetProperty("currentContextAlias").GetString());

        var items = data.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Contains(items.EnumerateArray(), item => item.GetProperty("contextAlias").GetString() == "rw14");
        Assert.Contains(items.EnumerateArray(), item => item.GetProperty("contextAlias").GetString() == "rw15");

        foreach (var item in items.EnumerateArray())
        {
            var loadedAtUnix = item.GetProperty("loadedAtUnix").GetInt64();
            Assert.InRange(loadedAtUnix, beforeLoad, afterList);
        }
    }

    [Fact]
    public void SelectContext_ChangesCurrentContextReportedByStatus()
    {
        // Arrange
        LoadAssemblyTool.LoadAssembly(assemblyPath: TestAssemblyLocator.GetPath(), contextAlias: "rw14", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(
            assemblyPath: typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
            contextAlias: "rw15",
            rebuildIndex: false,
            makeCurrent: false);

        // Act
        var selectResult = SelectContextTool.SelectContext("rw15");
        var statusResult = StatusTool.Status();

        // Assert
        Assert.DoesNotContain("error", selectResult, StringComparison.OrdinalIgnoreCase);

        var status = JsonSerializer.Deserialize<JsonElement>(statusResult);
        Assert.Equal("ok", status.GetProperty("status").GetString());

        var data = status.GetProperty("data");
        Assert.Equal("rw15", data.GetProperty("currentContextAlias").GetString());
        Assert.Equal(2, data.GetProperty("loadedContexts").GetArrayLength());
    }

    [Fact]
    public void Status_ReportsUnixTimestampForCurrentContext()
    {
        // Arrange
        var beforeLoad = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        LoadAssemblyTool.LoadAssembly(assemblyPath: TestAssemblyLocator.GetPath(), contextAlias: "rw14", rebuildIndex: false);

        // Act
        var statusResult = StatusTool.Status();
        var afterStatus = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Assert
        var status = JsonSerializer.Deserialize<JsonElement>(statusResult);
        Assert.Equal("ok", status.GetProperty("status").GetString());

        var startedAtUnix = status.GetProperty("data").GetProperty("startedAtUnix").GetInt64();
        Assert.InRange(startedAtUnix, beforeLoad, afterStatus);
    }

    [Fact]
    public void Unload_WithSpecificContextAlias_RemovesOnlyThatWorkspaceContext()
    {
        // Arrange
        LoadAssemblyTool.LoadAssembly(assemblyPath: TestAssemblyLocator.GetPath(), contextAlias: "rw14", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(
            assemblyPath: typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
            contextAlias: "rw15",
            rebuildIndex: false,
            makeCurrent: false);

        // Act
        var unloadResult = UnloadTool.Unload(contextAlias: "rw15");
        var contextsResult = ListContextsTool.ListContexts();

        // Assert
        var unloadResponse = JsonSerializer.Deserialize<JsonElement>(unloadResult);
        Assert.Equal("ok", unloadResponse.GetProperty("status").GetString());
        Assert.Equal("rw15", unloadResponse.GetProperty("data").GetProperty("unloaded").GetString());

        var contextsResponse = JsonSerializer.Deserialize<JsonElement>(contextsResult);
        var items = contextsResponse.GetProperty("data").GetProperty("items");
        Assert.Single(items.EnumerateArray());
        Assert.Equal("rw14", items[0].GetProperty("contextAlias").GetString());
    }

    [Fact]
    public void Unload_AllWorkspaceContexts_ClearsLoadedContextList()
    {
        // Arrange
        LoadAssemblyTool.LoadAssembly(assemblyPath: TestAssemblyLocator.GetPath(), contextAlias: "rw14", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(
            assemblyPath: typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
            contextAlias: "rw15",
            rebuildIndex: false,
            makeCurrent: false);

        // Act
        var unloadResult = UnloadTool.Unload(all: true);
        var statusResult = StatusTool.Status();

        // Assert
        var unloadResponse = JsonSerializer.Deserialize<JsonElement>(unloadResult);
        Assert.Equal("ok", unloadResponse.GetProperty("status").GetString());
        Assert.Equal("all", unloadResponse.GetProperty("data").GetProperty("unloaded").GetString());

        var statusResponse = JsonSerializer.Deserialize<JsonElement>(statusResult);
        var data = statusResponse.GetProperty("data");
        Assert.False(data.GetProperty("loaded").GetBoolean());
        Assert.False(data.TryGetProperty("currentContextAlias", out _));
        Assert.Empty(data.GetProperty("loadedContexts").EnumerateArray());
    }

    [Fact]
    public void GetServerStats_WithContextAlias_ReportsRequestedWorkspaceContext()
    {
        LoadAssemblyTool.LoadAssembly(assemblyPath: TestAssemblyLocator.GetPath(), contextAlias: "rw14", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(
            assemblyPath: typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
            contextAlias: "rw15",
            rebuildIndex: false,
            makeCurrent: false);

        var result = GetServerStatsTool.GetServerStats(contextAlias: "rw15");

        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal("rw15", data.GetProperty("contextAlias").GetString());
        Assert.Equal("rw14", data.GetProperty("currentContextAlias").GetString());
        Assert.Equal(2, data.GetProperty("loadedContexts").GetArrayLength());
    }

    public void Dispose()
    {
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

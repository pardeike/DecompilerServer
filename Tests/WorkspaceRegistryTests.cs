using System.Text.Json;
using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.Logging;

namespace Tests;

public class WorkspaceRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _registryPath;

    public WorkspaceRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DecompilerWorkspaceRegistry_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _registryPath = Path.Combine(_tempDir, "contexts.json");
    }

    [Fact]
    public void LoadAssembly_PersistsAliasRegistration()
    {
        using var workspace = new DecompilerWorkspace(_registryPath);

        workspace.LoadAssembly(new WorkspaceLoadRequest
        {
            AssemblyPath = TestAssemblyLocator.GetPath(),
            ContextAlias = "rw14",
            RebuildIndex = false,
            MakeCurrent = true
        });

        Assert.True(File.Exists(_registryPath));

        var json = File.ReadAllText(_registryPath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("rw14", root.GetProperty("currentContextAlias").GetString());
        Assert.Equal(1, root.GetProperty("contexts").GetArrayLength());
        Assert.Equal("rw14", root.GetProperty("contexts")[0].GetProperty("contextAlias").GetString());
        Assert.Equal(TestAssemblyLocator.GetPath(), root.GetProperty("contexts")[0].GetProperty("assemblyPath").GetString());
    }

    [Fact]
    public void RestoreRegisteredContexts_LoadsKnownAliasesAndRestoresCurrentSelection()
    {
        using (var firstWorkspace = new DecompilerWorkspace(_registryPath))
        {
            firstWorkspace.LoadAssembly(new WorkspaceLoadRequest
            {
                AssemblyPath = TestAssemblyLocator.GetPath(),
                ContextAlias = "rw14",
                RebuildIndex = false,
                MakeCurrent = true
            });

            firstWorkspace.LoadAssembly(new WorkspaceLoadRequest
            {
                AssemblyPath = typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
                ContextAlias = "rw15",
                RebuildIndex = false,
                MakeCurrent = false
            });
        }

        using var restartedWorkspace = new DecompilerWorkspace(_registryPath);

        var restoreResults = restartedWorkspace.RestoreRegisteredContexts();

        Assert.Equal(2, restoreResults.Count);
        Assert.All(restoreResults, result => Assert.True(result.Loaded, result.ErrorMessage));
        Assert.Equal("rw14", restartedWorkspace.CurrentContextAlias);
        Assert.Equal(2, restartedWorkspace.ListContexts().Count);
    }

    [Fact]
    public void UnloadContext_PersistsUpdatedCurrentSelection()
    {
        using var workspace = new DecompilerWorkspace(_registryPath);

        workspace.LoadAssembly(new WorkspaceLoadRequest
        {
            AssemblyPath = TestAssemblyLocator.GetPath(),
            ContextAlias = "rw14",
            RebuildIndex = false,
            MakeCurrent = true
        });

        workspace.LoadAssembly(new WorkspaceLoadRequest
        {
            AssemblyPath = typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
            ContextAlias = "rw15",
            RebuildIndex = false,
            MakeCurrent = false
        });

        workspace.UnloadContext("rw14");

        var json = File.ReadAllText(_registryPath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("rw15", workspace.CurrentContextAlias);
        Assert.Equal("rw15", root.GetProperty("currentContextAlias").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }
    }

    [Fact]
    public async Task WorkspaceBootstrapService_LogsRegisteredAliasesOnStartup()
    {
        using (var firstWorkspace = new DecompilerWorkspace(_registryPath))
        {
            firstWorkspace.LoadAssembly(new WorkspaceLoadRequest
            {
                AssemblyPath = TestAssemblyLocator.GetPath(),
                ContextAlias = "rw14",
                RebuildIndex = false,
                MakeCurrent = true
            });
        }

        using var restartedWorkspace = new DecompilerWorkspace(_registryPath);
        var logger = new ListLogger<WorkspaceBootstrapService>();
        using var bootstrapService = new WorkspaceBootstrapService(logger, restartedWorkspace);

        await bootstrapService.StartAsync(CancellationToken.None);
        await bootstrapService.StopAsync(CancellationToken.None);

        Assert.Contains(logger.Messages, message => message.Contains("rw14", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, message => message.Contains(TestAssemblyLocator.GetPath(), StringComparison.Ordinal));
    }
}

internal sealed class ListLogger<T> : ILogger<T>, IDisposable
{
    public List<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => this;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }

    public void Dispose()
    {
    }
}

using DecompilerServer.Services;

namespace Tests;

public class DecompilerWorkspaceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DecompilerWorkspace _workspace;

    public DecompilerWorkspaceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DecompilerWorkspaceTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _workspace = new DecompilerWorkspace(Path.Combine(_tempDir, "contexts.json"));
    }

    [Fact]
    public void LoadAssembly_WithDistinctAliases_KeepsMultipleContextsLoaded()
    {
        // Act
        var first = _workspace.LoadAssembly(new WorkspaceLoadRequest
        {
            AssemblyPath = TestAssemblyLocator.GetPath(),
            ContextAlias = "rw14",
            RebuildIndex = false,
            MakeCurrent = true
        });

        var second = _workspace.LoadAssembly(new WorkspaceLoadRequest
        {
            AssemblyPath = typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
            ContextAlias = "rw15",
            RebuildIndex = false,
            MakeCurrent = false
        });

        // Assert
        Assert.Equal("rw14", _workspace.CurrentContextAlias);

        var contexts = _workspace.ListContexts().OrderBy(c => c.ContextAlias).ToList();
        Assert.Equal(2, contexts.Count);

        Assert.Collection(contexts,
            item =>
            {
                Assert.Equal("rw14", item.ContextAlias);
                Assert.Equal(first.Mvid, item.Mvid);
            },
            item =>
            {
                Assert.Equal("rw15", item.ContextAlias);
                Assert.Equal(second.Mvid, item.Mvid);
            });
    }

    [Fact]
    public void SelectContext_ChangesCurrentContext_WithoutEvictingOthers()
    {
        // Arrange
        _workspace.LoadAssembly(new WorkspaceLoadRequest
        {
            AssemblyPath = TestAssemblyLocator.GetPath(),
            ContextAlias = "rw14",
            RebuildIndex = false,
            MakeCurrent = true
        });

        _workspace.LoadAssembly(new WorkspaceLoadRequest
        {
            AssemblyPath = typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
            ContextAlias = "rw15",
            RebuildIndex = false,
            MakeCurrent = false
        });

        // Act
        var selected = _workspace.SelectContext("rw15");

        // Assert
        Assert.Equal("rw15", _workspace.CurrentContextAlias);
        Assert.Equal("rw15", selected.ContextAlias);
        Assert.Equal(2, _workspace.ListContexts().Count());
    }

    [Fact]
    public void LoadAssembly_IntoExistingAlias_ReplacesOnlyThatAlias()
    {
        // Arrange
        var first = _workspace.LoadAssembly(new WorkspaceLoadRequest
        {
            AssemblyPath = TestAssemblyLocator.GetPath(),
            ContextAlias = "rw14",
            RebuildIndex = false,
            MakeCurrent = true
        });

        var second = _workspace.LoadAssembly(new WorkspaceLoadRequest
        {
            AssemblyPath = typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
            ContextAlias = "rw15",
            RebuildIndex = false,
            MakeCurrent = false
        });

        // Act
        var replacement = _workspace.LoadAssembly(new WorkspaceLoadRequest
        {
            AssemblyPath = typeof(global::NestedNoSymbolsTestLibrary.OuterContainer).Assembly.Location,
            ContextAlias = "rw15",
            RebuildIndex = false,
            MakeCurrent = false
        });

        // Assert
        var contexts = _workspace.ListContexts().OrderBy(c => c.ContextAlias).ToList();
        Assert.Equal(2, contexts.Count);
        Assert.Equal(first.Mvid, contexts[0].Mvid);
        Assert.Equal("rw14", contexts[0].ContextAlias);
        Assert.Equal("rw15", contexts[1].ContextAlias);
        Assert.Equal(replacement.Mvid, contexts[1].Mvid);
        Assert.NotEqual(second.Mvid, replacement.Mvid);
    }

    public void Dispose()
    {
        _workspace.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

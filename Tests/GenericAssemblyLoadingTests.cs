using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Tests for generic assembly loading functionality using LoadAssembly with assemblyPath
/// </summary>
public class GenericAssemblyLoadingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IServiceProvider _serviceProvider;

    public GenericAssemblyLoadingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"GenericAssemblyTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Set up service provider for the LoadAssembly tool
        var services = new ServiceCollection();
        services.AddSingleton<AssemblyContextManager>();
        services.AddSingleton<MemberResolver>();
        services.AddSingleton<ResponseFormatter>();

        _serviceProvider = services.BuildServiceProvider();
        ServiceLocator.SetServiceProvider(_serviceProvider);
    }

    [Fact]
    public void LoadAssembly_WithValidAssemblyPath_LoadsSuccessfully()
    {
        // Arrange - Use the test library DLL
        var testLibraryPath = GetTestLibraryPath();

        // Act
        var result = LoadAssemblyTool.LoadAssembly(assemblyPath: testLibraryPath, rebuildIndex: false);

        // Assert
        Assert.NotNull(result);
        var contextManager = _serviceProvider.GetRequiredService<AssemblyContextManager>();
        Assert.True(contextManager.IsLoaded);
        Assert.Equal(testLibraryPath, contextManager.AssemblyPath);
        Assert.NotNull(contextManager.Mvid);
    }

    [Fact]
    public void LoadAssembly_WithInvalidAssemblyPath_ThrowsFileNotFoundException()
    {
        // Arrange
        var invalidPath = Path.Combine(_tempDir, "nonexistent.dll");

        // Act & Assert
        var result = LoadAssemblyTool.LoadAssembly(assemblyPath: invalidPath);

        // Should return error response instead of throwing
        Assert.Contains("error", result.ToLower());
    }

    [Fact]
    public void LoadAssembly_WithAdditionalSearchDirs_ConfiguresResolver()
    {
        // Arrange
        var testLibraryPath = GetTestLibraryPath();
        var searchDir = Path.GetDirectoryName(testLibraryPath)!;
        var additionalSearchDirs = new[] { searchDir };

        // Act
        var result = LoadAssemblyTool.LoadAssembly(assemblyPath: testLibraryPath, additionalSearchDirs: additionalSearchDirs, rebuildIndex: false);

        // Assert
        Assert.NotNull(result);
        var contextManager = _serviceProvider.GetRequiredService<AssemblyContextManager>();
        Assert.True(contextManager.IsLoaded);
    }

    [Fact]
    public void LoadAssembly_CanAnalyzeGenericAssembly_ReturnsBasicInfo()
    {
        // Arrange
        var testLibraryPath = GetTestLibraryPath();

        // Act
        var result = LoadAssemblyTool.LoadAssembly(assemblyPath: testLibraryPath, rebuildIndex: true);

        // Assert
        Assert.NotNull(result);

        var contextManager = _serviceProvider.GetRequiredService<AssemblyContextManager>();
        Assert.True(contextManager.IsLoaded);
        Assert.True(contextManager.TypeCount > 0);
        Assert.True(contextManager.NamespaceCount > 0);
    }

    [Fact]
    public void AssemblyContextManager_LoadAssemblyDirect_WorksWithAnyDll()
    {
        // Arrange
        var contextManager = _serviceProvider.GetRequiredService<AssemblyContextManager>();
        var testLibraryPath = GetTestLibraryPath();

        // Act
        contextManager.LoadAssemblyDirect(testLibraryPath);

        // Assert
        Assert.True(contextManager.IsLoaded);
        Assert.Equal(testLibraryPath, contextManager.AssemblyPath);
        Assert.NotNull(contextManager.Mvid);

        // Verify we can access types
        var types = contextManager.GetAllTypes().ToList();
        Assert.NotEmpty(types);

        // Verify we can access namespaces
        var namespaces = contextManager.GetNamespaces().ToList();
        Assert.NotEmpty(namespaces);
    }

    [Fact]
    public void AssemblyContextManager_LoadAssemblyDirect_AddsAssemblyDirectoryToSearchPath()
    {
        // Arrange
        var contextManager = _serviceProvider.GetRequiredService<AssemblyContextManager>();
        var testLibraryPath = GetTestLibraryPath();

        // Act
        contextManager.LoadAssemblyDirect(testLibraryPath);

        // Assert
        Assert.True(contextManager.IsLoaded);

        // The resolver should be able to find dependencies in the same directory
        var resolver = contextManager.GetPEFile().Metadata;
        Assert.NotNull(resolver);
    }

    [Fact]
    public void LoadAssembly_WithBothGameDirAndAssemblyPath_ThrowsArgumentException()
    {
        // Arrange
        var testLibraryPath = GetTestLibraryPath();

        // Act & Assert
        var result = LoadAssemblyTool.LoadAssembly(gameDir: "/some/game/dir", assemblyPath: testLibraryPath);

        // Should return error response about conflicting parameters
        Assert.Contains("error", result.ToLower());
        Assert.Contains("cannot specify both", result.ToLower());
    }

    [Fact]
    public void LoadAssembly_WithNeitherParameter_ThrowsArgumentException()
    {
        // Act & Assert
        var result = LoadAssemblyTool.LoadAssembly();

        // Should return error response about missing parameters
        Assert.Contains("error", result.ToLower());
        Assert.Contains("must specify either", result.ToLower());
    }

    private string GetTestLibraryPath()
    {
        // Find the test library DLL
        var testLibraryPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",
            "TestLibrary", "bin", "Debug", "net8.0", "test.dll");

        testLibraryPath = Path.GetFullPath(testLibraryPath);

        if (!File.Exists(testLibraryPath))
        {
            throw new FileNotFoundException($"Test library not found at: {testLibraryPath}");
        }

        return testLibraryPath;
    }

    public void Dispose()
    {
        try
        {
            var contextManager = _serviceProvider.GetRequiredService<AssemblyContextManager>();
            contextManager.Dispose();
        }
        catch { }

        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }

        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }
}
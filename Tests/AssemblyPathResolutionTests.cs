using DecompilerServer.Services;
using System.Reflection;

namespace Tests;

/// <summary>
/// Tests for assembly path resolution logic to ensure Unity game directories are properly handled
/// </summary>
public class AssemblyPathResolutionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AssemblyContextManager _contextManager;

    public AssemblyPathResolutionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"DecompilerTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _contextManager = new AssemblyContextManager();
    }

    [Fact]
    public void ResolveAssemblyPath_WithGameNameWin64Data_ShouldFindAssembly()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDir, "RimWorld");
        var dataDir = Path.Combine(gameDir, "RimWorldWin64_Data", "Managed");
        var assemblyPath = Path.Combine(dataDir, "Assembly-CSharp.dll");

        Directory.CreateDirectory(dataDir);
        File.Copy(GetTestAssemblyPath(), assemblyPath);

        // Act - Load assembly using just the game root directory
        _contextManager.LoadAssembly(gameDir, "Assembly-CSharp.dll");

        // Assert
        Assert.True(_contextManager.IsLoaded);
        Assert.Equal(assemblyPath, _contextManager.AssemblyPath);
    }

    [Fact]
    public void ResolveAssemblyPath_WithGameNameWin32Data_ShouldFindAssembly()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDir, "MyGame");
        var dataDir = Path.Combine(gameDir, "MyGameWin32_Data", "Managed");
        var assemblyPath = Path.Combine(dataDir, "Assembly-CSharp.dll");

        Directory.CreateDirectory(dataDir);
        File.Copy(GetTestAssemblyPath(), assemblyPath);

        // Act
        _contextManager.LoadAssembly(gameDir, "Assembly-CSharp.dll");

        // Assert
        Assert.True(_contextManager.IsLoaded);
        Assert.Equal(assemblyPath, _contextManager.AssemblyPath);
    }

    [Fact]
    public void ResolveAssemblyPath_WithGameNameLinuxData_ShouldFindAssembly()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDir, "TestGame");
        var dataDir = Path.Combine(gameDir, "TestGameLinux_Data", "Managed");
        var assemblyPath = Path.Combine(dataDir, "Assembly-CSharp.dll");

        Directory.CreateDirectory(dataDir);
        File.Copy(GetTestAssemblyPath(), assemblyPath);

        // Act
        _contextManager.LoadAssembly(gameDir, "Assembly-CSharp.dll");

        // Assert
        Assert.True(_contextManager.IsLoaded);
        Assert.Equal(assemblyPath, _contextManager.AssemblyPath);
    }

    [Fact]
    public void ResolveAssemblyPath_WithOriginalPattern_ShouldStillWork()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDir, "ExistingGame");
        var dataDir = Path.Combine(gameDir, "ExistingGame_Data", "Managed");
        var assemblyPath = Path.Combine(dataDir, "Assembly-CSharp.dll");

        Directory.CreateDirectory(dataDir);
        File.Copy(GetTestAssemblyPath(), assemblyPath);

        // Act
        _contextManager.LoadAssembly(gameDir, "Assembly-CSharp.dll");

        // Assert
        Assert.True(_contextManager.IsLoaded);
        Assert.Equal(assemblyPath, _contextManager.AssemblyPath);
    }

    [Fact]
    public void ResolveAssemblyPath_WithDirectManagedFolder_ShouldWork()
    {
        // Arrange
        var gameDir = Path.Combine(_tempDir, "DirectGame");
        var managedDir = Path.Combine(gameDir, "Managed");
        var assemblyPath = Path.Combine(managedDir, "Assembly-CSharp.dll");

        Directory.CreateDirectory(managedDir);
        File.Copy(GetTestAssemblyPath(), assemblyPath);

        // Act
        _contextManager.LoadAssembly(gameDir, "Assembly-CSharp.dll");

        // Assert
        Assert.True(_contextManager.IsLoaded);
        Assert.Equal(assemblyPath, _contextManager.AssemblyPath);
    }

    [Fact]
    public void ResolveAssemblyPath_WithAbsolutePath_ShouldUseDirectly()
    {
        // Arrange
        var absolutePath = GetTestAssemblyPath();

        // Act
        _contextManager.LoadAssembly("/some/game/dir", absolutePath);

        // Assert
        Assert.True(_contextManager.IsLoaded);
        Assert.Equal(absolutePath, _contextManager.AssemblyPath);
    }

    [Fact]
    public void ResolveAssemblyPath_WithRimWorldExample_ShouldFindAssembly()
    {
        // Arrange - simulate the exact RimWorld scenario from the issue
        var gameDir = Path.Combine(_tempDir, "RimWorld");
        var dataDir = Path.Combine(gameDir, "RimWorldWin64_Data", "Managed");
        var assemblyPath = Path.Combine(dataDir, "Assembly-CSharp.dll");

        Directory.CreateDirectory(dataDir);
        File.Copy(GetTestAssemblyPath(), assemblyPath);

        // Act - Use only the game root directory as the user wants
        _contextManager.LoadAssembly(gameDir, "Assembly-CSharp.dll");

        // Assert
        Assert.True(_contextManager.IsLoaded);
        Assert.Equal(assemblyPath, _contextManager.AssemblyPath);
    }

    private string GetTestAssemblyPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "TestLibrary", "bin", "Debug", "net8.0", "test.dll"));
    }

    public void Dispose()
    {
        _contextManager?.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}
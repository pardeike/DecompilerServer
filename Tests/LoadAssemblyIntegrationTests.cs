using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Integration test to verify the LoadAssembly MCP tool works with enhanced path resolution
/// </summary>
public class LoadAssemblyIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IServiceProvider _serviceProvider;

    public LoadAssemblyIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LoadAssemblyTest_{Guid.NewGuid():N}");
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
    public void LoadAssemblyTool_WithRimWorldGameDir_ShouldSucceed()
    {
        // Arrange - Create RimWorld-like directory structure
        var gameDir = Path.Combine(_tempDir, "RimWorld");
        var dataDir = Path.Combine(gameDir, "RimWorldWin64_Data", "Managed");
        Directory.CreateDirectory(dataDir);

        var testAssemblyPath = GetTestAssemblyPath();
        var targetAssemblyPath = Path.Combine(dataDir, "Assembly-CSharp.dll");
        File.Copy(testAssemblyPath, targetAssemblyPath);

        // Act - Use LoadAssembly tool with just the game directory
        var result = LoadAssemblyTool.LoadAssembly(gameDir: gameDir, assemblyFile: "Assembly-CSharp.dll");

        // Assert
        Assert.NotNull(result);
        Assert.DoesNotContain("error", result);
        Assert.Contains("mvid", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assemblyPath", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(targetAssemblyPath.Replace("\\", "/"), result.Replace("\\", "/"));
    }

    [Fact]
    public void LoadAssemblyTool_WithOriginalPattern_ShouldStillWork()
    {
        // Arrange - Create traditional Unity directory structure
        var gameDir = Path.Combine(_tempDir, "MyGame");
        var dataDir = Path.Combine(gameDir, "MyGame_Data", "Managed");
        Directory.CreateDirectory(dataDir);

        var testAssemblyPath = GetTestAssemblyPath();
        var targetAssemblyPath = Path.Combine(dataDir, "Assembly-CSharp.dll");
        File.Copy(testAssemblyPath, targetAssemblyPath);

        // Act
        var result = LoadAssemblyTool.LoadAssembly(gameDir: gameDir, assemblyFile: "Assembly-CSharp.dll");

        // Assert
        Assert.NotNull(result);
        Assert.DoesNotContain("error", result);
        Assert.Contains("mvid", result, StringComparison.OrdinalIgnoreCase);
    }

    private string GetTestAssemblyPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "TestLibrary", "bin", "Debug", "net8.0", "test.dll"));
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
                // Ignore cleanup errors
            }
        }
    }
}
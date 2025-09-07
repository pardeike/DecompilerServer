using DecompilerServer.Services;

namespace DecompilerServer.Tests;

/// <summary>
/// Base class for all service tests, provides common setup and teardown for testing with test.dll
/// </summary>
public class ServiceTestBase : IDisposable
{
    protected AssemblyContextManager ContextManager { get; }
    protected MemberResolver MemberResolver { get; }
    protected string TestAssemblyPath { get; }

    protected ServiceTestBase()
    {
        ContextManager = new AssemblyContextManager();
        MemberResolver = new MemberResolver(ContextManager);
        
        // Get path to the test.dll we built
        TestAssemblyPath = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "TestLibrary", "bin", "Debug", "net8.0", "test.dll"));
        
        // Load the test assembly for all tests
        if (File.Exists(TestAssemblyPath))
        {
            // Create a temporary directory to simulate a "game directory"
            var tempDir = Path.GetTempPath();
            ContextManager.LoadAssembly(tempDir, TestAssemblyPath);
        }
    }

    public void Dispose()
    {
        ContextManager?.Dispose();
    }
}
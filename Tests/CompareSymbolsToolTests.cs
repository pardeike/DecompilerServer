using System.Text.Json;
using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class CompareSymbolsToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _registryPath;
    private readonly IServiceProvider _serviceProvider;

    public CompareSymbolsToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CompareSymbols_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _registryPath = Path.Combine(_tempDir, "contexts.json");

        var services = new ServiceCollection();
        services.AddSingleton(_ => new DecompilerWorkspace(_registryPath));
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
    public void CompareSymbols_TypeDiff_ReturnsMemberSurfaceOverview()
    {
        var leftAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "VersionedLeft",
            """
            namespace VersionedApi;

            public class SampleType
            {
                public int Value;

                public void Alpha()
                {
                }
            }
            """);

        var rightAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "VersionedRight",
            """
            namespace VersionedApi;

            public class SampleType
            {
                public string Value = "";

                public void Beta()
                {
                }
            }
            """);

        LoadAssemblyTool.LoadAssembly(assemblyPath: leftAssembly, contextAlias: "left", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(assemblyPath: rightAssembly, contextAlias: "right", rebuildIndex: false, makeCurrent: false);

        var result = CompareSymbolsTool.CompareSymbols("left", "right", "VersionedApi.SampleType", symbolKind: "type");

        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal("Type", data.GetProperty("symbolKind").GetString());
        Assert.True(data.GetProperty("leftExists").GetBoolean());
        Assert.True(data.GetProperty("rightExists").GetBoolean());

        Assert.Contains(data.GetProperty("addedMembers").EnumerateArray(), item => item.GetProperty("name").GetString() == "Beta");
        Assert.Contains(data.GetProperty("removedMembers").EnumerateArray(), item => item.GetProperty("name").GetString() == "Alpha");
        Assert.Contains(data.GetProperty("changedMembers").EnumerateArray(), item => item.GetProperty("name").GetString() == "Value");
    }

    [Fact]
    public void CompareSymbols_MethodDiff_ReturnsSignatureSummary()
    {
        var leftAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "MethodLeft",
            """
            namespace VersionedApi;

            public class Worker
            {
                public int Compute(int value)
                {
                    return value + 1;
                }
            }
            """);

        var rightAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "MethodRight",
            """
            namespace VersionedApi;

            public class Worker
            {
                public string Compute(string value)
                {
                    return value + "!";
                }
            }
            """);

        LoadAssemblyTool.LoadAssembly(assemblyPath: leftAssembly, contextAlias: "left", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(assemblyPath: rightAssembly, contextAlias: "right", rebuildIndex: false, makeCurrent: false);

        var result = CompareSymbolsTool.CompareSymbols("left", "right", "VersionedApi.Worker:Compute", symbolKind: "method");

        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal("Method", data.GetProperty("symbolKind").GetString());
        Assert.Equal("surface", data.GetProperty("compareMode").GetString());
        Assert.True(data.GetProperty("leftExists").GetBoolean());
        Assert.True(data.GetProperty("rightExists").GetBoolean());
        Assert.True(data.GetProperty("signatureChanged").GetBoolean());
        Assert.Contains("Compute", data.GetProperty("leftSignature").GetString());
        Assert.Contains("Compute", data.GetProperty("rightSignature").GetString());
        Assert.False(data.TryGetProperty("bodyDiff", out _));
    }

    [Fact]
    public void CompareSymbols_MethodBodyDiff_WhenRequested_ReturnsUnifiedBodyDiff()
    {
        var leftAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "MethodBodyLeft",
            """
            namespace VersionedApi;

            public class Worker
            {
                public int Compute(int value)
                {
                    var adjusted = value + 1;
                    return adjusted;
                }
            }
            """);

        var rightAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "MethodBodyRight",
            """
            namespace VersionedApi;

            public class Worker
            {
                public int Compute(int value)
                {
                    var adjusted = value + 2;
                    return adjusted * 2;
                }
            }
            """);

        LoadAssemblyTool.LoadAssembly(assemblyPath: leftAssembly, contextAlias: "left", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(assemblyPath: rightAssembly, contextAlias: "right", rebuildIndex: false, makeCurrent: false);

        var result = CompareSymbolsTool.CompareSymbols(
            "left",
            "right",
            "VersionedApi.Worker:Compute",
            symbolKind: "method",
            compareMode: "body");

        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal("Method", data.GetProperty("symbolKind").GetString());
        Assert.Equal("body", data.GetProperty("compareMode").GetString());
        Assert.True(data.GetProperty("leftExists").GetBoolean());
        Assert.True(data.GetProperty("rightExists").GetBoolean());
        Assert.False(data.GetProperty("signatureChanged").GetBoolean());
        Assert.True(data.GetProperty("bodyChanged").GetBoolean());

        var diffStats = data.GetProperty("diffStats");
        Assert.True(diffStats.GetProperty("addedLines").GetInt32() > 0);
        Assert.True(diffStats.GetProperty("removedLines").GetInt32() > 0);
        Assert.True(diffStats.GetProperty("changedBlocks").GetInt32() > 0);

        var bodyDiff = data.GetProperty("bodyDiff").GetString();
        Assert.NotNull(bodyDiff);
        Assert.Contains("- ", bodyDiff, StringComparison.Ordinal);
        Assert.Contains("+ ", bodyDiff, StringComparison.Ordinal);
        Assert.Contains("value + 1", bodyDiff, StringComparison.Ordinal);
        Assert.Contains("value + 2", bodyDiff, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareSymbols_MethodBodyDiff_AcceptsSourceAliasAndDottedMemberFormat()
    {
        var leftAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "MethodSourceAliasLeft",
            """
            namespace VersionedApi;

            public class Worker
            {
                public int Compute(int value)
                {
                    return value + 1;
                }
            }
            """);

        var rightAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "MethodSourceAliasRight",
            """
            namespace VersionedApi;

            public class Worker
            {
                public int Compute(int value)
                {
                    return value + 2;
                }
            }
            """);

        LoadAssemblyTool.LoadAssembly(assemblyPath: leftAssembly, contextAlias: "left", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(assemblyPath: rightAssembly, contextAlias: "right", rebuildIndex: false, makeCurrent: false);

        var result = CompareSymbolsTool.CompareSymbols(
            "left",
            "right",
            "VersionedApi.Worker.Compute",
            symbolKind: "method",
            compareMode: "source");

        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal("body", data.GetProperty("compareMode").GetString());
        Assert.True(data.GetProperty("bodyChanged").GetBoolean());
        Assert.Contains("value + 1", data.GetProperty("bodyDiff").GetString(), StringComparison.Ordinal);
        Assert.Contains("value + 2", data.GetProperty("bodyDiff").GetString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

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
}

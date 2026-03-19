using System.Text.Json;
using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class ContextAwareToolRoutingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IServiceProvider _serviceProvider;

    public ContextAwareToolRoutingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ContextAwareToolRouting_{Guid.NewGuid():N}");
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
    public void SearchTypes_WithContextAlias_TargetsRequestedLoadedVersion()
    {
        // Arrange
        LoadAssemblyTool.LoadAssembly(assemblyPath: TestAssemblyLocator.GetPath(), contextAlias: "rw14", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(
            assemblyPath: typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
            contextAlias: "rw15",
            rebuildIndex: false,
            makeCurrent: false);

        // Act
        var rw14Result = SearchTypesTool.SearchTypes("Simple", contextAlias: "rw14", limit: 10);
        var rw15Result = SearchTypesTool.SearchTypes("Simple", contextAlias: "rw15", limit: 10);

        // Assert
        var rw14Response = JsonSerializer.Deserialize<JsonElement>(rw14Result);
        Assert.Equal("ok", rw14Response.GetProperty("status").GetString());
        Assert.True(rw14Response.GetProperty("data").GetProperty("items").GetArrayLength() > 0);

        var rw15Response = JsonSerializer.Deserialize<JsonElement>(rw15Result);
        Assert.Equal("ok", rw15Response.GetProperty("status").GetString());
        Assert.Equal(0, rw15Response.GetProperty("data").GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void GetDecompiledSource_WithForeignCurrentContext_RoutesByMemberIdMvid()
    {
        // Arrange
        LoadAssemblyTool.LoadAssembly(assemblyPath: TestAssemblyLocator.GetPath(), contextAlias: "rw14", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(
            assemblyPath: typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
            contextAlias: "rw15",
            rebuildIndex: false,
            makeCurrent: true);

        var workspace = _serviceProvider.GetRequiredService<DecompilerWorkspace>();
        Assert.True(workspace.TryGetSession("rw14", out var rw14Session));

        var simpleType = rw14Session.ContextManager.GetAllTypes().First(type => type.FullName == "TestLibrary.SimpleClass");
        var memberId = rw14Session.MemberResolver.GenerateMemberId(simpleType);

        SelectContextTool.SelectContext("rw15");

        // Act
        var result = GetDecompiledSourceTool.GetDecompiledSource(memberId, includeHeader: false);

        // Assert
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var lines = response.GetProperty("data").GetProperty("lines").EnumerateArray().Select(line => line.GetString()).ToArray();
        Assert.Contains(lines, line => line != null && line.Contains("public class SimpleClass", StringComparison.Ordinal));
    }

    [Fact]
    public void GenerateExtensionMethodWrapper_WithForeignCurrentContext_UsesOwningSessionResolverForTargetMetadata()
    {
        var (memberId, expectedSignature) = LoadCrossContextMethod(requireInstanceMethod: true);

        var result = GenerateExtensionMethodWrapperTool.GenerateExtensionMethodWrapper(memberId);

        AssertTargetMetadataMatches(result, memberId, expectedSignature);
    }

    [Fact]
    public void GenerateDetourStub_WithForeignCurrentContext_UsesOwningSessionResolverForTargetMetadata()
    {
        var (memberId, expectedSignature) = LoadCrossContextMethod(requireInstanceMethod: false);

        var result = GenerateDetourStubTool.GenerateDetourStub(memberId);

        AssertTargetMetadataMatches(result, memberId, expectedSignature);
    }

    [Fact]
    public void GenerateHarmonyPatchSkeleton_WithForeignCurrentContext_UsesOwningSessionResolverForTargetMetadata()
    {
        var (memberId, expectedSignature) = LoadCrossContextMethod(requireInstanceMethod: false);

        var result = GenerateHarmonyPatchSkeletonTool.GenerateHarmonyPatchSkeleton(memberId, patchKinds: "Prefix", includeReflectionTargeting: true);

        AssertTargetMetadataMatches(result, memberId, expectedSignature);
    }

    private (string MemberId, string ExpectedSignature) LoadCrossContextMethod(bool requireInstanceMethod)
    {
        LoadAssemblyTool.LoadAssembly(assemblyPath: TestAssemblyLocator.GetPath(), contextAlias: "rw14", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(
            assemblyPath: typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location,
            contextAlias: "rw15",
            rebuildIndex: false,
            makeCurrent: true);

        var workspace = _serviceProvider.GetRequiredService<DecompilerWorkspace>();
        Assert.True(workspace.TryGetSession("rw14", out var rw14Session));

        var testType = rw14Session.ContextManager.GetAllTypes().First(type => type.FullName == "TestLibrary.SimpleClass");
        var method = requireInstanceMethod
            ? testType.Methods.First(candidate => !candidate.IsStatic && !candidate.IsConstructor)
            : testType.Methods.First(candidate => !candidate.IsConstructor);

        var memberId = rw14Session.MemberResolver.GenerateMemberId(method);
        var signature = rw14Session.MemberResolver.GetMemberSignature(method);

        SelectContextTool.SelectContext("rw15");

        return (memberId, signature);
    }

    private static void AssertTargetMetadataMatches(string result, string expectedMemberId, string expectedSignature)
    {
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var target = response.GetProperty("data").GetProperty("target");
        Assert.Equal(expectedMemberId, target.GetProperty("memberId").GetString());
        Assert.Equal(expectedSignature, target.GetProperty("signature").GetString());
    }

    public void Dispose()
    {
        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

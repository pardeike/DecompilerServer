using System.Text.Json;
using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServiceLocatorIsolationCollection
{
    public const string Name = "ServiceLocator isolation";
}

[Collection(ServiceLocatorIsolationCollection.Name)]
public sealed class WorkspaceMemoryLimitTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _registryPath;

    public WorkspaceMemoryLimitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"WorkspaceMemoryLimitTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _registryPath = Path.Combine(_tempDir, "contexts.json");
    }

    [Fact]
    public void LoadingBeyondLimit_EvictsLeastRecentlyUsedIdleContext_AndReloadsItOnDemand()
    {
        using var workspace = new DecompilerWorkspace(_registryPath, maxLoadedContexts: 2);
        Load(workspace, "first", TestAssemblyLocator.GetPath(), makeCurrent: true);
        Load(workspace, "second", EmbeddedAssemblyPath, makeCurrent: false);

        DecompilerSession secondSession;
        using (var secondLease = workspace.AcquireSession("second"))
        {
            secondSession = secondLease.Session;
        }

        using (workspace.AcquireSession("first"))
        {
            // Touch first so second is the least recently used context.
        }

        Load(workspace, "third", NestedAssemblyPath, makeCurrent: false);

        Assert.Equal(["first", "third"], LoadedAliases(workspace));
        Assert.Equal(["first", "second", "third"], workspace.ListRegisteredAliases());
        Assert.False(secondSession.ContextManager.IsLoaded);
        Assert.True(secondSession.DecompilerService.IsDisposed);
        Assert.Equal(1, workspace.GetMemoryStats().Evictions);

        using var reloadedSecond = workspace.AcquireSession("second");

        Assert.Equal("second", reloadedSecond.Session.ContextAlias);
        Assert.True(reloadedSecond.Session.ContextManager.IsLoaded);
        Assert.Equal(["second", "third"], LoadedAliases(workspace));
        Assert.Equal("first", workspace.CurrentContextAlias);

        var memory = workspace.GetMemoryStats();
        Assert.Equal(2, memory.MaxLoadedContexts);
        Assert.Equal(2, memory.LoadedContexts);
        Assert.Equal(2, memory.Evictions);
        Assert.Equal(1, memory.Reloads);
    }

    [Fact]
    public void LoadingAtCapacity_NeverEvictsLeasedContexts_AndReturnsStructuredBusyError()
    {
        using var workspace = new DecompilerWorkspace(_registryPath, maxLoadedContexts: 2);
        Load(workspace, "first", TestAssemblyLocator.GetPath(), makeCurrent: true);
        Load(workspace, "second", EmbeddedAssemblyPath, makeCurrent: false);

        using var firstLease = workspace.AcquireSession("first");
        var unloadException = Assert.Throws<ToolErrorException>(() => workspace.UnloadContext("first"));
        Assert.Equal("context_busy", unloadException.Code);

        Load(workspace, "third", NestedAssemblyPath, makeCurrent: false);

        Assert.True(firstLease.Session.ContextManager.IsLoaded);
        Assert.Equal(["first", "third"], LoadedAliases(workspace));

        using var thirdLease = workspace.AcquireSession("third");
        var exception = Assert.Throws<ToolErrorException>(() =>
            Load(workspace, "fourth", EmbeddedAssemblyPath, makeCurrent: false));

        Assert.Equal("context_capacity_busy", exception.Code);
        Assert.Equal(["first", "third"], LoadedAliases(workspace));
        Assert.DoesNotContain("fourth", workspace.ListRegisteredAliases());

        var memory = workspace.GetMemoryStats();
        Assert.Equal(2, memory.LoadedContexts);
        Assert.Equal(2, memory.ActiveLeases);
        Assert.Equal(["first", "third"], memory.LeasedAliases);
    }

    [Fact]
    public void InvalidReplacement_PreflightLeavesLruSessionUntouched()
    {
        using var workspace = new DecompilerWorkspace(_registryPath, maxLoadedContexts: 2);
        Load(workspace, "first", TestAssemblyLocator.GetPath(), makeCurrent: true);
        Load(workspace, "second", EmbeddedAssemblyPath, makeCurrent: false);

        DecompilerSession firstSession;
        using (var firstLease = workspace.AcquireSession("first"))
        {
            firstSession = firstLease.Session;
        }

        using (workspace.AcquireSession("second"))
        {
            // Keep first as the LRU candidate while retaining its identity for the assertion.
        }

        var missingPath = Path.Combine(_tempDir, "missing.dll");
        Assert.Throws<FileNotFoundException>(() =>
            Load(workspace, "missing", missingPath, makeCurrent: false));

        var corruptPath = Path.Combine(_tempDir, "corrupt.dll");
        File.WriteAllText(corruptPath, "not a managed assembly");
        Assert.Throws<BadImageFormatException>(() =>
            Load(workspace, "corrupt", corruptPath, makeCurrent: false));

        using var unchangedFirstLease = workspace.AcquireSession("first");
        Assert.Same(firstSession, unchangedFirstLease.Session);
        Assert.True(firstSession.ContextManager.IsLoaded);
        Assert.Equal(["first", "second"], LoadedAliases(workspace));
        Assert.Equal(0, workspace.GetMemoryStats().Evictions);
        Assert.DoesNotContain("missing", workspace.ListRegisteredAliases());
        Assert.DoesNotContain("corrupt", workspace.ListRegisteredAliases());
    }

    [Fact]
    public void PostPreflightLoadFailure_RestoresDisplacedLruAndCurrentAlias()
    {
        DecompilerSession CreateSession(string contextAlias, WorkspaceLoadRequest request)
        {
            if (string.Equals(contextAlias, "replacement", StringComparison.Ordinal))
                throw new InvalidOperationException("Injected session construction failure.");

            return DecompilerSession.Create(contextAlias, request);
        }

        using var workspace = new DecompilerWorkspace(_registryPath, maxLoadedContexts: 2, sessionFactory: CreateSession);
        Load(workspace, "first", TestAssemblyLocator.GetPath(), makeCurrent: true);
        Load(workspace, "second", EmbeddedAssemblyPath, makeCurrent: false);

        DecompilerSession displacedSession;
        using (var firstLease = workspace.AcquireSession("first"))
        {
            displacedSession = firstLease.Session;
            var settings = displacedSession.ContextManager.GetSettings();
            settings.UsingDeclarations = false;
            displacedSession.ContextManager.UpdateSettings(settings);
        }

        using (workspace.AcquireSession("second"))
        {
            // Make first the LRU victim before injecting the post-preflight failure.
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Load(workspace, "replacement", NestedAssemblyPath, makeCurrent: true));

        Assert.Equal("Injected session construction failure.", exception.Message);
        Assert.False(displacedSession.ContextManager.IsLoaded);
        Assert.Equal(["first", "second"], LoadedAliases(workspace));
        Assert.Equal("first", workspace.CurrentContextAlias);
        Assert.DoesNotContain("replacement", workspace.ListRegisteredAliases());

        using var restoredLease = workspace.AcquireSession("first");
        Assert.NotSame(displacedSession, restoredLease.Session);
        Assert.True(restoredLease.Session.ContextManager.IsLoaded);
        Assert.False(restoredLease.Session.ContextManager.GetSettings().UsingDeclarations);

        var memory = workspace.GetMemoryStats();
        Assert.Equal(2, memory.LoadedContexts);
        Assert.Equal(1, memory.Evictions);
        Assert.Equal(1, memory.Reloads);
    }

    [Fact]
    public void RegistryPersistenceFailure_RestoresDisplacedResidentAndLeavesRegistryUnchanged()
    {
        using var workspace = new DecompilerWorkspace(_registryPath, maxLoadedContexts: 2);
        Load(workspace, "first", TestAssemblyLocator.GetPath(), makeCurrent: true);
        Load(workspace, "second", EmbeddedAssemblyPath, makeCurrent: false);

        DecompilerSession displacedSession;
        using (var firstLease = workspace.AcquireSession("first"))
        {
            displacedSession = firstLease.Session;
            var settings = displacedSession.ContextManager.GetSettings();
            settings.UsingDeclarations = false;
            displacedSession.ContextManager.UpdateSettings(settings);
        }

        using (workspace.AcquireSession("second"))
        {
            // Keep first as the LRU victim while retaining its identity for the assertion.
        }

        File.Delete(_registryPath);
        Directory.CreateDirectory(_registryPath);

        var exception = Record.Exception(() =>
            Load(workspace, "replacement", NestedAssemblyPath, makeCurrent: true));

        Assert.NotNull(exception);
        Assert.True(exception is IOException or UnauthorizedAccessException, exception.ToString());
        Assert.False(displacedSession.ContextManager.IsLoaded);
        Assert.True(displacedSession.DecompilerService.IsDisposed);
        Assert.Equal(["first", "second"], LoadedAliases(workspace));
        Assert.Equal(["first", "second"], workspace.ListRegisteredAliases());
        Assert.Equal("first", workspace.CurrentContextAlias);
        Assert.DoesNotContain("replacement", workspace.ListRegisteredAliases());

        using var restoredLease = workspace.AcquireSession("first");
        Assert.NotSame(displacedSession, restoredLease.Session);
        Assert.False(restoredLease.Session.ContextManager.GetSettings().UsingDeclarations);

        var memory = workspace.GetMemoryStats();
        Assert.Equal(2, memory.LoadedContexts);
        Assert.Equal(1, memory.Evictions);
        Assert.Equal(1, memory.Reloads);
    }

    [Fact]
    public async Task ConcurrentLease_PreventsEvictionUntilTheUsingCallCompletes()
    {
        using var workspace = new DecompilerWorkspace(_registryPath, maxLoadedContexts: 2);
        Load(workspace, "first", TestAssemblyLocator.GetPath(), makeCurrent: true);
        Load(workspace, "second", EmbeddedAssemblyPath, makeCurrent: false);

        var leaseAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeCall = Task.Run(async () =>
        {
            using var lease = workspace.AcquireSession("first");
            leaseAcquired.SetResult();
            await releaseLease.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(lease.Session.ContextManager.IsLoaded);
        });

        await leaseAcquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Load(workspace, "third", NestedAssemblyPath, makeCurrent: false);

        Assert.Equal(["first", "third"], LoadedAliases(workspace));
        Assert.Equal(1, workspace.GetMemoryStats().ActiveLeases);

        releaseLease.SetResult();
        await activeCall.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, workspace.GetMemoryStats().ActiveLeases);
    }

    [Fact]
    public void MemberIdFromEvictedContext_ReloadsOwningAliasInsteadOfFallingBackToCurrent()
    {
        using var workspace = new DecompilerWorkspace(_registryPath, maxLoadedContexts: 1);
        Load(workspace, "owner", TestAssemblyLocator.GetPath(), makeCurrent: true);

        string memberId;
        using (var ownerLease = workspace.AcquireSession("owner"))
        {
            var type = ownerLease.Session.ContextManager.FindTypeByName("TestLibrary.SimpleClass");
            Assert.NotNull(type);
            memberId = ownerLease.Session.MemberResolver.GenerateMemberId(type);
        }

        Load(workspace, "current", EmbeddedAssemblyPath, makeCurrent: true);
        Assert.Equal(["current"], LoadedAliases(workspace));

        using var routedLease = workspace.AcquireSessionForMemberId(memberId);

        Assert.Equal("owner", routedLease.Session.ContextAlias);
        Assert.Equal(["owner"], LoadedAliases(workspace));
        Assert.Equal("current", workspace.CurrentContextAlias);
        Assert.Equal(2, workspace.GetMemoryStats().Evictions);
        Assert.Equal(1, workspace.GetMemoryStats().Reloads);
    }

    [Fact]
    public void EvictionAndReload_PreservePerContextDecompilerSettings()
    {
        using var workspace = new DecompilerWorkspace(_registryPath, maxLoadedContexts: 1);
        Load(workspace, "configured", TestAssemblyLocator.GetPath(), makeCurrent: true);

        using (var configuredLease = workspace.AcquireSession("configured"))
        {
            var settings = configuredLease.Session.ContextManager.GetSettings();
            settings.UsingDeclarations = false;
            settings.ShowXmlDocumentation = false;
            configuredLease.Session.ContextManager.UpdateSettings(settings);
        }

        Load(workspace, "other", EmbeddedAssemblyPath, makeCurrent: true);

        using var reloadedLease = workspace.AcquireSession("configured");
        var restoredSettings = reloadedLease.Session.ContextManager.GetSettings();

        Assert.False(restoredSettings.UsingDeclarations);
        Assert.False(restoredSettings.ShowXmlDocumentation);
    }

    [Fact]
    public void CompareContexts_AtTwoContextLimit_HoldsBothLeasesForTheOperation()
    {
        using var workspace = new DecompilerWorkspace(_registryPath, maxLoadedContexts: 2);
        using var serviceProvider = CreateServiceProvider(workspace);
        ServiceLocator.SetServiceProvider(serviceProvider);

        Load(workspace, "left", TestAssemblyLocator.GetPath(), makeCurrent: true);
        Load(workspace, "right", EmbeddedAssemblyPath, makeCurrent: false);

        var result = CompareContextsTool.CompareContexts("left", "right", limit: 10);
        var response = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.Equal("ok", response.GetProperty("status").GetString());
        Assert.Equal(0, workspace.GetMemoryStats().ActiveLeases);
        Assert.Equal(["left", "right"], LoadedAliases(workspace));

        var statsResult = GetServerStatsTool.GetServerStats("left");
        var statsResponse = JsonSerializer.Deserialize<JsonElement>(statsResult);
        var memory = statsResponse.GetProperty("data").GetProperty("memory");
        Assert.Equal(2, memory.GetProperty("maxLoadedContexts").GetInt32());
        Assert.Equal(2, memory.GetProperty("loadedContexts").GetInt32());
        Assert.Equal(0, memory.GetProperty("activeLeases").GetInt32());
    }

    [Fact]
    public void BatchGetDecompiledSource_ReleasesEarlierContextLeaseAfterItsResultIsCopied()
    {
        using var workspace = new DecompilerWorkspace(_registryPath, maxLoadedContexts: 1);
        using var serviceProvider = CreateServiceProvider(workspace);
        ServiceLocator.SetServiceProvider(serviceProvider);

        Load(workspace, "first", TestAssemblyLocator.GetPath(), makeCurrent: true);
        string firstMemberId;
        using (var firstLease = workspace.AcquireSession("first"))
        {
            var firstType = firstLease.Session.ContextManager.GetAllTypes()
                .First(type => type.FullName == "TestLibrary.SimpleClass");
            firstMemberId = firstLease.Session.MemberResolver.GenerateMemberId(firstType);
        }

        Load(workspace, "second", EmbeddedAssemblyPath, makeCurrent: false);
        string secondMemberId;
        using (var secondLease = workspace.AcquireSession("second"))
        {
            var secondType = secondLease.Session.ContextManager.GetAllTypes()
                .First(type => type.FullName == "EmbeddedSourceTestLibrary.EmbeddedSourceSample");
            secondMemberId = secondLease.Session.MemberResolver.GenerateMemberId(secondType);
        }

        var result = BatchGetDecompiledSourceTool.BatchGetDecompiledSource(
            [firstMemberId, secondMemberId],
            maxTotalChars: 50_000);
        var response = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.Equal("ok", response.GetProperty("status").GetString());
        var items = response.GetProperty("data").GetProperty("items");
        Assert.True(items[0].TryGetProperty("code", out _));
        Assert.False(items[0].TryGetProperty("error", out _));
        Assert.True(items[1].TryGetProperty("code", out _));
        Assert.False(items[1].TryGetProperty("error", out _));
        Assert.Equal(["second"], LoadedAliases(workspace));
        Assert.Equal(0, workspace.GetMemoryStats().ActiveLeases);
    }

    [Fact]
    public void RepeatedLoads_KeepResidentContextCountAtHardLimit()
    {
        using var workspace = new DecompilerWorkspace(_registryPath, maxLoadedContexts: 2);
        var paths = new[] { TestAssemblyLocator.GetPath(), EmbeddedAssemblyPath, NestedAssemblyPath };

        for (var index = 0; index < 12; index++)
        {
            Load(workspace, $"context-{index}", paths[index % paths.Length], makeCurrent: index == 0);
            Assert.InRange(workspace.ListContexts().Count, 1, 2);
        }

        var memory = workspace.GetMemoryStats();
        Assert.Equal(2, memory.LoadedContexts);
        Assert.Equal(10, memory.Evictions);
        Assert.Equal(12, workspace.ListRegisteredAliases().Count);
    }

    private static string EmbeddedAssemblyPath =>
        typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location;

    private static string NestedAssemblyPath =>
        typeof(global::NestedNoSymbolsTestLibrary.OuterContainer).Assembly.Location;

    private static void Load(
        DecompilerWorkspace workspace,
        string contextAlias,
        string assemblyPath,
        bool makeCurrent)
    {
        workspace.LoadAssembly(new WorkspaceLoadRequest
        {
            AssemblyPath = assemblyPath,
            ContextAlias = contextAlias,
            RebuildIndex = false,
            MakeCurrent = makeCurrent
        });
    }

    private static string[] LoadedAliases(DecompilerWorkspace workspace)
    {
        return workspace.ListContexts()
            .Select(context => context.ContextAlias)
            .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ServiceProvider CreateServiceProvider(DecompilerWorkspace workspace)
    {
        var services = new ServiceCollection();
        services.AddSingleton(workspace);
        services.AddSingleton<AssemblyContextManager>();
        services.AddSingleton<MemberResolver>();
        services.AddSingleton<DecompilerService>();
        services.AddSingleton<UsageAnalyzer>();
        services.AddSingleton<InheritanceAnalyzer>();
        services.AddSingleton<ResponseFormatter>();
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

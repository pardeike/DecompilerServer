using System.Text.Json;
using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// <c>scope: "assembly"</c> only ever searches the assembly that owns the member, so an empty
/// result is ambiguous: the member may well be called from a dependent assembly that happens not to
/// be loaded. Callers used to read that as "nobody calls this"; the tool now says so explicitly.
/// These tests pin the hint down for <c>find_callers</c> and <c>find_usages</c>.
/// </summary>
public class ReferenceScopeHintTests : IDisposable
{
    private const string ProducerSource =
        """
        namespace Scope
        {
            public class Producer
            {
                public void Produce()
                {
                }
            }
        }
        """;

    private const string ConsumerSource =
        """
        namespace Scope
        {
            public class Consumer
            {
                public void Call(Producer producer)
                {
                    producer.Produce();
                }
            }
        }
        """;

    private readonly string _tempDir;
    private readonly string _registryPath;
    private readonly IServiceProvider _serviceProvider;
    private string _producerPath = string.Empty;

    public ReferenceScopeHintTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RefScope_{Guid.NewGuid():N}");
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

    /// <summary>
    /// Loads the producer assembly as "producer" and the caller assembly as "caller", then returns
    /// the memberId of <c>Producer.Produce</c>. The caller assembly really calls it, so a
    /// workspace-scoped search has something to find and an assembly-scoped one has nothing.
    /// </summary>
    private string LoadPairWithCrossAssemblyCaller()
    {
        _producerPath = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "ScopeProducer", ProducerSource);
        var consumerPath = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "ScopeConsumer", ConsumerSource, [_producerPath]);

        LoadAssemblyTool.LoadAssembly(assemblyPath: _producerPath, contextAlias: "producer", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(assemblyPath: consumerPath, contextAlias: "caller", rebuildIndex: false, makeCurrent: false);

        using var lease = ServiceLocator.Workspace!.AcquireSession("producer");
        return lease.Session.MemberResolver.GenerateMemberId(
            lease.Session.ContextManager.GetAllTypes()
                .Single(type => type.FullName == "Scope.Producer")
                .Methods.Single(method => method.Name == "Produce"));
    }

    [Fact]
    public void FindCallers_AssemblyScopeWithoutHits_HintsAtWorkspaceScope()
    {
        var produce = LoadPairWithCrossAssemblyCaller();

        var response = JsonSerializer.Deserialize<JsonElement>(FindCallersTool.FindCallers(produce, limit: 10));
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal("assembly", data.GetProperty("scope").GetString());
        Assert.Empty(data.GetProperty("items").EnumerateArray());

        var hint = data.GetProperty("hint").GetString();
        Assert.NotNull(hint);
        Assert.Contains("No references inside assembly 'producer'", hint!, StringComparison.Ordinal);
        Assert.Contains("scope:\"workspace\"", hint!, StringComparison.Ordinal);
        Assert.Contains("caller", hint!, StringComparison.Ordinal);
    }

    [Fact]
    public void FindCallers_WorkspaceScope_FindsCrossAssemblyCallerWithoutHint()
    {
        var produce = LoadPairWithCrossAssemblyCaller();

        var response = JsonSerializer.Deserialize<JsonElement>(
            FindCallersTool.FindCallers(produce, limit: 10, scope: "workspace"));
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal("workspace", data.GetProperty("scope").GetString());

        var callers = data.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(callers);
        Assert.Equal("Scope.Consumer", callers[0].GetProperty("inType").GetString());
        Assert.Equal("Call", callers[0].GetProperty("memberName").GetString());

        // A real answer needs no hint.
        Assert.False(data.TryGetProperty("hint", out _));
    }

    [Fact]
    public void FindUsages_AssemblyScopeWithoutHits_HintsAtWorkspaceScope()
    {
        var produce = LoadPairWithCrossAssemblyCaller();

        var response = JsonSerializer.Deserialize<JsonElement>(FindUsagesTool.FindUsages(produce, limit: 10));
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Empty(data.GetProperty("items").EnumerateArray());

        var hint = data.GetProperty("hint").GetString();
        Assert.NotNull(hint);
        Assert.Contains("No references inside assembly 'producer'", hint!, StringComparison.Ordinal);
        Assert.Contains("scope:\"workspace\"", hint!, StringComparison.Ordinal);
    }

    [Fact]
    public void FindCallers_AssemblyScopeWithHits_OmitsHint()
    {
        // Both the definition and its call live in the same assembly: no ambiguity, no hint.
        var library = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "ScopeSelfCall",
            """
            namespace ScopeSelf
            {
                public class Callee
                {
                    public void Run()
                    {
                    }
                }

                public class Caller
                {
                    public void Call(Callee callee)
                    {
                        callee.Run();
                    }
                }
            }
            """);

        LoadAssemblyTool.LoadAssembly(assemblyPath: library, contextAlias: "selfcall", rebuildIndex: false);

        string run;
        using (var lease = ServiceLocator.Workspace!.AcquireSession("selfcall"))
        {
            run = lease.Session.MemberResolver.GenerateMemberId(
                lease.Session.ContextManager.GetAllTypes()
                    .Single(type => type.FullName == "ScopeSelf.Callee")
                    .Methods.Single(method => method.Name == "Run"));
        }

        var response = JsonSerializer.Deserialize<JsonElement>(FindCallersTool.FindCallers(run, limit: 10));
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.False(data.TryGetProperty("hint", out _));
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
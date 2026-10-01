using System.Text.Json;
using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class ExplorationAssemblyFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "DecompilerExploration_" + Guid.NewGuid().ToString("N"));
    public string Provider { get; }
    public string OtherVersion { get; }
    public string Consumer { get; }

    public ExplorationAssemblyFixture()
    {
        const string source = """
            using System.Runtime.CompilerServices;
            namespace Exploration;
            public struct PlainA { public int Value; }
            public struct PlainB { public int Value; }
            public class Surface {
                public int Property { get; set; }
                public int Computed => Targets.Field;
                public System.Collections.Generic.Dictionary<string, int?> Signature<T>(System.Span<int> values, ref int a, out string b, in double c) { b = "text"; return new(); }
            }
            [CompilerGenerated] public class GeneratedContainer { public class Nested { public void Hidden() { } } }
            public static class Targets {
                public static int Field;
                public static void Call(int value) { Field = value; }
                public static void Call(string value) { Field = value.Length; }
            }
            public static class Generic<T> { public static U Echo<U>(T value, U result) => result; }
            """;
        Provider = TemporaryAssemblyBuilder.BuildLibrary(Root, "ExplorationProvider", source);
        OtherVersion = TemporaryAssemblyBuilder.BuildLibrary(Path.Combine(Root, "other"), "ExplorationProvider", source.Replace("Field = value;", "Field = value + 1;"));
        Consumer = TemporaryAssemblyBuilder.BuildLibrary(Root, "ExplorationConsumer", """
            namespace Exploration;
            public static class Consumer {
                public static int ReadProperty(Surface surface) => surface.Property;
                public static void Run() {
                    Targets.Call(1); Targets.Call("example");
                    Generic<int>.Echo<string>(1, "result");
                    int value = Targets.Field; Targets.Field = value + 1;
                }
            }
            """, [Provider]);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

[Collection(ServiceLocatorIsolationCollection.Name)]
public sealed class ExplorationRegressionToolTests : IClassFixture<ExplorationAssemblyFixture>, IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly DecompilerWorkspace _workspace;

    public ExplorationRegressionToolTests(ExplorationAssemblyFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_ => new DecompilerWorkspace(Path.Combine(fixture.Root, Guid.NewGuid() + ".json")));
        _provider = services.BuildServiceProvider();
        ServiceLocator.SetServiceProvider(_provider);
        _workspace = _provider.GetRequiredService<DecompilerWorkspace>();
        foreach (var (alias, path) in new[] { ("provider", fixture.Provider), ("consumer", fixture.Consumer), ("other", fixture.OtherVersion) })
            _workspace.LoadAssembly(new WorkspaceLoadRequest { AssemblyPath = path, ContextAlias = alias, PersistRegistration = true, MakeCurrent = alias == "provider" });
    }

    private static JsonElement Response(string json) => JsonSerializer.Deserialize<JsonElement>(json);
    private static JsonElement Data(string json) => Response(json).GetProperty("data");
    private string MethodId(string type, string name, string? parameter = null, string alias = "provider")
    {
        using var lease = _workspace.AcquireSession(alias);
        var method = lease.Session.ContextManager.GetAllTypes().Single(t => t.FullName == type).Methods
            .Single(m => m.Name == name && (parameter == null || m.Parameters[0].Type.Name == parameter));
        return lease.Session.MemberResolver.GenerateMemberId(method);
    }

    [Fact]
    public void SyntheticConstructors_AreNeverReturnedOrResolvedToAnotherType()
    {
        var items = Data(SearchSymbolsTool.SearchSymbols("Plain", contextAlias: "provider", includeCompilerGenerated: true)).GetProperty("items");
        foreach (var item in items.EnumerateArray())
        {
            var id = item.GetProperty("memberId").GetString()!;
            Assert.DoesNotContain(":00000000:", id);
            Assert.Equal("ok", Response(GetMemberDetailsTool.GetMemberDetails(id)).GetProperty("status").GetString());
        }
        using var lease = _workspace.AcquireSession("provider");
        var zeroId = lease.Session.ContextManager.Mvid!.Replace("-", "") + ":00000000:M";
        var invalid = Response(GetDecompiledSourceTool.GetDecompiledSource(zeroId));
        Assert.Equal("error", invalid.GetProperty("status").GetString());
        Assert.Equal("invalid_member_id", invalid.GetProperty("error").GetProperty("code").GetString());
        var structs = lease.Session.ContextManager.GetAllTypes().Where(t => t.Name.StartsWith("Plain"));
        foreach (var type in structs)
            Assert.DoesNotContain(Data(ListMembersTool.ListMembers(lease.Session.MemberResolver.GenerateMemberId(type), includeCompilerGenerated: true)).GetProperty("items").EnumerateArray(),
                item => item.GetProperty("memberId").GetString()!.Contains(":00000000:"));
    }

    [Fact]
    public void GeneratedDescendantsAndAccessors_AreHiddenByDefaultButAvailableExplicitly()
    {
        Assert.Empty(Data(SearchSymbolsTool.SearchSymbols("GeneratedContainer", contextAlias: "provider")).GetProperty("items").EnumerateArray());
        Assert.NotEmpty(Data(SearchSymbolsTool.SearchSymbols("GeneratedContainer", contextAlias: "provider", includeCompilerGenerated: true)).GetProperty("items").EnumerateArray());
        Assert.Empty(Data(SearchTypesTool.SearchTypes("Nested", contextAlias: "provider")).GetProperty("items").EnumerateArray());
        Assert.NotEmpty(Data(SearchTypesTool.SearchTypes("Nested", contextAlias: "provider", includeCompilerGenerated: true)).GetProperty("items").EnumerateArray());
        var ordinary = Data(ListMembersTool.ListMembers("T:Exploration.Surface", contextAlias: "provider")).GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(ordinary, item => item.GetProperty("name").GetString() == "Property");
        Assert.DoesNotContain(ordinary, item => item.GetProperty("name").GetString() is "get_Property" or "set_Property" or "<Property>k__BackingField");
        var full = Data(ListMembersTool.ListMembers("T:Exploration.Surface", contextAlias: "provider", includeCompilerGenerated: true)).GetProperty("items");
        Assert.Contains(full.EnumerateArray(), item => item.GetProperty("name").GetString() == "get_Property");
        Assert.All(full.EnumerateArray(), item => Assert.Equal("ok", Response(GetMemberDetailsTool.GetMemberDetails(item.GetProperty("memberId").GetString()!)).GetProperty("status").GetString()));
    }

    [Fact]
    public void Signatures_PreserveGenericsReferenceModifiersAndOwner()
    {
        var result = Data(SearchMembersTool.SearchMembers("Signature", contextAlias: "provider", mode: "signatures")).GetProperty("items")[0];
        Assert.Equal("Exploration.Surface", result.GetProperty("declaringType").GetString());
        var signature = result.GetProperty("signature").GetString()!;
        Assert.Contains("Dictionary<String, Int32?>", signature);
        Assert.Contains("Signature<T>", signature);
        Assert.Contains("Span<Int32>", signature);
        Assert.Contains("ref Int32 a", signature);
        Assert.Contains("out String b", signature);
        Assert.Contains("in Double c", signature);
    }

    [Fact]
    public void Batch_SourceContinuationAndBudgetExhaustionAreSeparate()
    {
        var partial = Data(BatchGetDecompiledSourceTool.BatchGetDecompiledSource(["T:Exploration.Surface"], contextAlias: "provider", maxLinesPerMember: 2));
        Assert.True(partial.GetProperty("truncated").GetBoolean());
        Assert.False(partial.GetProperty("budgetTruncated").GetBoolean());
        var item = partial.GetProperty("items")[0];
        Assert.True(item.GetProperty("hasMore").GetBoolean());
        Assert.Equal(3, item.GetProperty("nextStartLine").GetInt32());
        var full = Data(BatchGetDecompiledSourceTool.BatchGetDecompiledSource(["T:Exploration.Surface"], contextAlias: "provider", maxLinesPerMember: 1000));
        Assert.False(full.GetProperty("truncated").GetBoolean());
        Assert.False(full.GetProperty("items")[0].GetProperty("hasMore").GetBoolean());
        var exhausted = Data(BatchGetDecompiledSourceTool.BatchGetDecompiledSource(["T:Exploration.Surface"], contextAlias: "provider", maxTotalChars: 1));
        Assert.True(exhausted.GetProperty("budgetTruncated").GetBoolean());
    }

    [Fact]
    public void WorkspaceReferences_MatchOverloadsGenericsAndExactAssemblyVersion()
    {
        var id = MethodId("Exploration.Targets", "Call", "Int32");
        var local = Data(FindCallersTool.FindCallers(id));
        Assert.Equal("assembly", local.GetProperty("scope").GetString());
        Assert.Empty(local.GetProperty("items").EnumerateArray());
        var workspace = Data(FindCallersTool.FindCallers(id, scope: "workspace"));
        var caller = Assert.Single(workspace.GetProperty("items").EnumerateArray());
        Assert.Equal("Exploration.Consumer", caller.GetProperty("inType").GetString());
        Assert.True(caller.GetProperty("offset").GetInt32() >= 0);
        Assert.Equal(3, workspace.GetProperty("searchedContexts").GetArrayLength());
        Assert.Empty(Data(FindCallersTool.FindCallers(MethodId("Exploration.Targets", "Call", "Int32", "other"), scope: "workspace")).GetProperty("items").EnumerateArray());
        var generic = Data(FindCallersTool.FindCallers(MethodId("Exploration.Generic", "Echo"), scope: "workspace"));
        Assert.Single(generic.GetProperty("items").EnumerateArray());
        var usages = Data(FindUsagesTool.FindUsages("F:Exploration.Targets.Field", contextAlias: "provider", scope: "workspace", limit: 1));
        Assert.True(usages.GetProperty("hasMore").GetBoolean());
        var next = Data(FindUsagesTool.FindUsages("F:Exploration.Targets.Field", contextAlias: "provider", scope: "workspace", limit: 1, cursor: usages.GetProperty("nextCursor").GetString()));
        Assert.NotEqual(usages.GetProperty("items")[0].ToString(), next.GetProperty("items")[0].ToString());
        Assert.Contains(Data(FindUsagesTool.FindUsages("F:Exploration.Targets.Field", contextAlias: "provider", scope: "workspace")).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("memberName").GetString() == "get_Computed");
        Assert.Equal("error", Response(FindCallersTool.FindCallers(id, scope: "everything")).GetProperty("status").GetString());
    }

    [Fact]
    public void Callees_LinkLoadedDependenciesAndKeepIdentityWhenDependencyIsUnloaded()
    {
        var id = MethodId("Exploration.Consumer", "Run", alias: "consumer");
        var linked = Data(FindCalleesTool.FindCallees(id)).GetProperty("items").EnumerateArray().ToArray();
        var calls = linked.Where(item => item.GetProperty("kind").GetString() == "Call").ToArray();
        Assert.Equal(3, calls.Length);
        Assert.All(calls, item => Assert.Equal("workspace", item.GetProperty("resolution").GetString()));
        Assert.All(calls, item => Assert.Equal("ok", Response(GetMemberDetailsTool.GetMemberDetails(item.GetProperty("targetMemberId").GetString()!)).GetProperty("status").GetString()));
        _workspace.UnloadContext("provider");
        var unlinked = Data(FindCalleesTool.FindCallees(id)).GetProperty("items").EnumerateArray().Where(item => item.GetProperty("kind").GetString() == "Call");
        Assert.All(unlinked, item =>
        {
            Assert.Equal("external", item.GetProperty("resolution").GetString());
            Assert.False(item.TryGetProperty("targetMemberId", out _));
            Assert.Contains("ExplorationProvider", item.GetProperty("referencedAssembly").GetString()!);
        });
    }

    [Fact]
    public void WorkspaceQueries_DoNotActivateDeferredContextsAndReleaseAllLeases()
    {
        _workspace.UnloadContext("other", preserveRegistration: true);
        var selection = _workspace.CurrentContextAlias;
        var data = Data(FindUsagesTool.FindUsages("P:Exploration.Surface.Property", contextAlias: "provider", scope: "workspace"));
        var usage = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("ReadProperty", usage.GetProperty("memberName").GetString());
        Assert.Equal(2, data.GetProperty("searchedContexts").GetArrayLength());
        Assert.Equal(2, _workspace.ListContexts().Count);
        Assert.Equal(selection, _workspace.CurrentContextAlias);
        Assert.Contains("other", _workspace.ListRegisteredAliases());
        Assert.Equal(0, _workspace.GetMemoryStats().ActiveLeases);
        FindCalleesTool.FindCallees(MethodId("Exploration.Consumer", "Run", alias: "consumer"));
        Assert.Equal(0, _workspace.GetMemoryStats().ActiveLeases);
        _workspace.UnloadContext("consumer");
    }

    [Fact]
    public void CompactLiterals_ContainOneCopyOfEachString()
    {
        var items = Data(SearchStringLiteralsTool.SearchStringLiterals("example", contextAlias: "consumer")).GetProperty("items");
        Assert.NotEmpty(items.EnumerateArray());
        Assert.All(items.EnumerateArray(), item => Assert.False(item.TryGetProperty("snippet", out _)));
    }

    public void Dispose() => _provider.Dispose();
}

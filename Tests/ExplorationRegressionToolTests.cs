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
    public string LeftSurface { get; }
    public string RightSurface { get; }

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
                public int WithHeader(System.Text.StringBuilder value) => value.Length;
                public static int Arguments(int a, int b, int c, int d, int e, int last) => last;
                public event System.Action? Changed;
            }
            [CompilerGenerated] public class GeneratedContainer { public class Nested { public void Hidden() { } } }
            public static class Targets {
                public static int Field;
                public static void Call(int value) { Field = value; }
                public static void Call(string value) { Field = value.Length; }
            }
            public static class Generic<T> { public static U Echo<U>(T value, U result) => result; }
            public class ManyMembers {
                public void M00() {} public void M01() {} public void M02() {} public void M03() {}
                public void M04() {} public void M05() {} public void M06() {} public void M07() {}
                public void M08() {} public void M09() {} public void M10() {} public void M11() {}
            }
            """;
        Provider = TemporaryAssemblyBuilder.BuildLibrary(Root, "ExplorationProvider", source);
        OtherVersion = TemporaryAssemblyBuilder.BuildLibrary(Path.Combine(Root, "other"), "ExplorationProvider", source.Replace("Field = value;", "Field = value + 1;"));
        Consumer = TemporaryAssemblyBuilder.BuildLibrary(Root, "ExplorationConsumer", """
            namespace Exploration;
            public class Derived : Surface { public int Local() => 222; }
            public static class Consumer {
                public static int ReadProperty(Surface surface) => surface.Property;
                public static void Run() {
                    Targets.Call(1); Targets.Call("example");
                    Generic<int>.Echo<string>(1, "result");
                    int value = Targets.Field; Targets.Field = value + 1;
                }
            }
            """, [Provider]);
        const string surface = "namespace Alpha { public class Payload {} } namespace Beta { public class Payload {} } namespace Versioned { public class Api { public Alpha.Payload Get() => null; public int Value { get; set; } public int Run() => 1; } }";
        LeftSurface = TemporaryAssemblyBuilder.BuildLibrary(Root, "SurfaceLeft", surface);
        RightSurface = TemporaryAssemblyBuilder.BuildLibrary(Root, "SurfaceRight", surface
            .Replace("Alpha.Payload Get", "Beta.Payload Get")
            .Replace("get; set;", "get;")
            .Replace("public int Run", "public static int Run"));
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}

[Collection(ServiceLocatorIsolationCollection.Name)]
public sealed class ExplorationRegressionToolTests : IClassFixture<ExplorationAssemblyFixture>, IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly DecompilerWorkspace _workspace;
    private readonly ExplorationAssemblyFixture _fixture;

    public ExplorationRegressionToolTests(ExplorationAssemblyFixture fixture)
    {
        _fixture = fixture;
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

    [Fact]
    public void DependencyBodies_NeverReadTokensFromTheConsumer()
    {
        foreach (var response in new[] {
            GetDecompiledSourceTool.GetDecompiledSource("M:Exploration.Surface.WithHeader", contextAlias: "consumer"),
            GetILTool.GetIL("M:Exploration.Surface.WithHeader", contextAlias: "consumer") })
        {
            var result = Response(response);
            Assert.Equal("error", result.GetProperty("status").GetString());
            Assert.Equal("member_context_mismatch", result.GetProperty("error").GetProperty("code").GetString());
            Assert.NotEmpty(result.GetProperty("error").GetProperty("hints").EnumerateArray());
        }
    }

    [Theory]
    [InlineData("Exploration.Targets.Call(System.String)")]
    [InlineData("M:Exploration.Targets.Call(System.String)")]
    [InlineData("Exploration.Targets:Call(string)")]
    public void Resolution_SelectsTheRequestedOverload(string input)
    {
        var result = Data(ResolveMemberIdTool.ResolveMemberId(input, "provider"));
        Assert.Equal(MethodId("Exploration.Targets", "Call", "String"), result.GetProperty("memberId").GetString());
    }

    [Fact]
    public void XmlSignatures_PreserveConstructorsGenericsAndByReferenceParameters()
    {
        using var lease = _workspace.AcquireSession("provider");
        var type = lease.Session.ContextManager.GetAllTypes().Single(type => type.FullName == "Exploration.Surface");
        foreach (var method in type.Methods.Where(method => method.Name == "Signature" || method.IsConstructor))
        {
            var xml = ICSharpCode.Decompiler.Documentation.IdStringProvider.GetIdString(method);
            var result = Data(ResolveMemberIdTool.ResolveMemberId(xml, "provider"));
            Assert.Equal(lease.Session.MemberResolver.GenerateMemberId(method), result.GetProperty("memberId").GetString());
        }
        Assert.Equal("error", Response(ResolveMemberIdTool.ResolveMemberId("F:Exploration.Surface.WithHeader", "provider")).GetProperty("status").GetString());
    }

    [Fact]
    public void Resolution_ReportsAmbiguityInsteadOfChoosingAnOverload()
    {
        foreach (var response in new[] {
            ResolveMemberIdTool.ResolveMemberId("Exploration.Targets.Call", "provider"),
            NormalizeMemberIdTool.NormalizeMemberId("Exploration.Targets.Call", "provider"),
            CompareSymbolsTool.CompareSymbols("provider", "other", "Exploration.Targets:Call", "method", "body") })
        {
            var result = Response(response);
            Assert.Equal("error", result.GetProperty("status").GetString());
            Assert.Equal("ambiguous_member", result.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal(2, result.GetProperty("error").GetProperty("details").GetProperty("candidates").GetArrayLength());
        }
        var comparison = Data(CompareSymbolsTool.CompareSymbols("provider", "other", "Exploration.Targets:Call(System.Int32)", "method", "body"));
        Assert.True(comparison.GetProperty("bodyChanged").GetBoolean());
    }

    [Fact]
    public void Batch_ContinuationUsesTheSameSourceCoordinatesAsSlices()
    {
        var id = MethodId("Exploration.Surface", "WithHeader");
        var first = Data(BatchGetDecompiledSourceTool.BatchGetDecompiledSource([id], maxLinesPerMember: 2)).GetProperty("items")[0];
        var rest = Data(GetSourceSliceTool.GetSourceSlice(id, first.GetProperty("nextStartLine").GetInt32(), 1000));
        var full = Data(GetSourceSliceTool.GetSourceSlice(id, 1, 1000));
        Assert.Equal(full.GetProperty("totalLines").GetInt32(), first.GetProperty("totalLines").GetInt32());
        Assert.Equal(full.GetProperty("code").GetString(), first.GetProperty("code").GetString() + "\n" + rest.GetProperty("code").GetString());
    }

    [Fact]
    public void CompareSurface_DetectsQualifiedReturnTypesModifiersAndAccessors()
    {
        foreach (var (alias, path) in new[] { ("left", _fixture.LeftSurface), ("right", _fixture.RightSurface) })
            _workspace.LoadAssembly(new WorkspaceLoadRequest { AssemblyPath = path, ContextAlias = alias, MakeCurrent = false });
        var diff = Data(CompareSymbolsTool.CompareSymbols("left", "right", "Versioned.Api", "type"));
        foreach (var name in new[] { "Get", "Run", "Value" })
            Assert.Contains(diff.GetProperty("changedMembers").EnumerateArray(), item => item.GetProperty("name").GetString() == name);
        var overview = Data(CompareContextsTool.CompareContexts("left", "right", "Versioned"));
        Assert.Equal(1, overview.GetProperty("summary").GetProperty("changedTypes").GetInt32());
    }

    [Fact]
    public void Batch_ReleasesEachContextBeforeLoadingTheNext()
    {
        var ids = new List<string>();
        foreach (var (alias, path, type) in new[] {
            ("provider", _fixture.Provider, "Exploration.Surface"), ("consumer", _fixture.Consumer, "Exploration.Consumer"),
            ("other", _fixture.OtherVersion, "Exploration.Surface"), ("left", _fixture.LeftSurface, "Versioned.Api"),
            ("right", _fixture.RightSurface, "Versioned.Api") })
        {
            _workspace.LoadAssembly(new WorkspaceLoadRequest { AssemblyPath = path, ContextAlias = alias, MakeCurrent = false });
            using var lease = _workspace.AcquireSession(alias);
            ids.Add(lease.Session.MemberResolver.NormalizeMemberId("T:" + type));
        }
        var items = Data(BatchGetDecompiledSourceTool.BatchGetDecompiledSource(ids.ToArray(), maxLinesPerMember: 2)).GetProperty("items");
        Assert.Equal(5, items.GetArrayLength());
        Assert.All(items.EnumerateArray(), item => Assert.False(item.TryGetProperty("error", out _)));
        Assert.Equal(0, _workspace.GetMemoryStats().ActiveLeases);
    }

    [Fact]
    public void DependencyIds_ProvideLoadingHintsInsteadOfHumanNameDiagnostics()
    {
        var bases = Data(FindBaseTypesTool.FindBaseTypes("T:Exploration.Derived", contextAlias: "consumer")).GetProperty("bases");
        var id = bases[0].GetProperty("memberId").GetString()!;
        _workspace.UnloadContext("provider");
        var error = Response(GetMemberDetailsTool.GetMemberDetails(id)).GetProperty("error");
        Assert.Equal("context_not_loaded", error.GetProperty("code").GetString());
        var hint = Assert.Single(error.GetProperty("hints").EnumerateArray(), hint => hint.GetProperty("tool").GetString() == "load_assembly");
        var path = hint.GetProperty("arguments").GetProperty("assemblyPath").GetString()!;
        Assert.True(File.Exists(path));
        Assert.Equal("ok", Response(LoadAssemblyTool.LoadAssembly(assemblyPath: path, contextAlias: "recovered", makeCurrent: false)).GetProperty("status").GetString());
        Assert.Equal("ok", Response(GetMemberDetailsTool.GetMemberDetails(id)).GetProperty("status").GetString());
    }

    [Fact]
    public void MetadataOutline_ReturnsAddressableMembersAndExplicitPagination()
    {
        var first = Data(GetAstOutlineTool.GetAstOutline("T:Exploration.ManyMembers", contextAlias: "provider")).GetProperty("outline");
        Assert.True(first.TryGetProperty("hasMore", out _));
        var children = first.GetProperty("children").EnumerateArray().ToArray();
        Assert.True(children.Length >= 13);
        Assert.All(children, child => Assert.Equal("ok", Response(GetMemberDetailsTool.GetMemberDetails(child.GetProperty("memberId").GetString()!)).GetProperty("status").GetString()));
    }

    [Fact]
    public void IlOperands_DistinguishArgumentsFromLocals()
    {
        var instructions = Data(GetILTool.GetIL("M:Exploration.Surface.Arguments", contextAlias: "provider")).GetProperty("instructions");
        var argument = Assert.Single(instructions.EnumerateArray(), i => i.GetProperty("opCode").GetString() == "ldarg.s");
        Assert.Equal("argument", argument.GetProperty("operandKind").GetString());
        Assert.Equal("A_5", argument.GetProperty("operandSummary").GetString());
    }

    [Fact]
    public void SessionIndexes_AreReusedAcrossQueriesAndInvalidatedByClear()
    {
        using var lease = _workspace.AcquireSession("provider");
        for (var index = 0; index < 2; index++)
        {
            SearchTypesTool.SearchTypes("Surface", contextAlias: "provider");
            SearchMembersTool.SearchMembers("WithHeader", contextAlias: "provider");
            SearchSymbolsTool.SearchSymbols("Surface", contextAlias: "provider");
        }
        var search = lease.Session.SearchService.GetSearchCacheStats();
        Assert.Equal(2, search.CachedSearches);
        Assert.Equal(1, search.SymbolIndexBuilds);
        FindUsagesTool.FindUsages(MethodId("Exploration.Targets", "Call", "Int32"));
        FindUsagesTool.FindUsages(MethodId("Exploration.Targets", "Call", "String"));
        SearchStringLiteralsTool.SearchStringLiterals("text", contextAlias: "provider");
        SearchStringLiteralsTool.SearchStringLiterals("other", contextAlias: "provider");
        var usage = lease.Session.UsageAnalyzer.GetCacheStats();
        Assert.Equal(1, usage.ReferenceIndexBuilds);
        Assert.Equal(1, usage.LiteralIndexBuilds);
        Assert.True(usage.EstimatedBytes > 0);
        ClearCachesTool.ClearCaches(contextAlias: "provider");
        Assert.Equal(0, lease.Session.SearchService.GetSearchCacheStats().SymbolIndexBuilds);
        Assert.Equal(0, lease.Session.UsageAnalyzer.GetCacheStats().ReferenceIndexBuilds);
        SearchSymbolsTool.SearchSymbols("Surface", contextAlias: "provider");
        Assert.Equal(1, lease.Session.SearchService.GetSearchCacheStats().SymbolIndexBuilds);
    }

    [Fact]
    public void Listings_ExposePagingWithoutDumpingRegisteredHistoryByDefault()
    {
        var contexts = Data(ListContextsTool.ListContexts());
        Assert.Equal(3, contexts.GetProperty("registeredAliasCount").GetInt32());
        Assert.False(contexts.TryGetProperty("registeredAliases", out _));
        var aliases = Data(ListContextsTool.ListContexts(includeRegistered: true, limit: 1, filter: "o"));
        Assert.Single(aliases.GetProperty("registeredAliases").EnumerateArray());
        Assert.True(aliases.GetProperty("hasMore").GetBoolean());
        var next = Data(ListContextsTool.ListContexts(includeRegistered: true, limit: 1, filter: "o", cursor: aliases.GetProperty("nextCursor").GetString()));
        Assert.NotEqual(aliases.GetProperty("registeredAliases")[0].GetString(), next.GetProperty("registeredAliases")[0].GetString());
        var outline = Data(GetAstOutlineTool.GetAstOutline("T:Exploration.ManyMembers", contextAlias: "provider", limit: 5)).GetProperty("outline");
        Assert.Equal(5, outline.GetProperty("children").GetArrayLength());
        Assert.True(outline.GetProperty("hasMore").GetBoolean());
        var following = Data(GetAstOutlineTool.GetAstOutline("T:Exploration.ManyMembers", contextAlias: "provider", limit: 5, cursor: outline.GetProperty("nextCursor").GetString())).GetProperty("outline");
        Assert.NotEqual(outline.GetProperty("children")[0].GetProperty("memberId").GetString(), following.GetProperty("children")[0].GetProperty("memberId").GetString());
    }

    public void Dispose() => _provider.Dispose();
}

using System.Text.Json;
using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Regression tests for generic/non-generic name collisions.
///
/// ITypeDefinition.FullName renders both <c>IFoo</c> and <c>IFoo&lt;T&gt;</c> as "N.IFoo", so keying
/// a comparison by FullName makes ToDictionary throw "An item with the same key has already been
/// added" and makes a plain-name lookup silently resolve to whichever arity happened to come first.
/// Real-world trigger: obfuscated .NET Framework libraries such as Topomatic.*, where dozens of the
/// assemblies declare both shapes side by side.
///
/// Companion test: CompareContextsToolTests.CompareContexts_WithGenericAndNonGenericTwin_DoesNotCollide
/// covers the unchanged-twin case with the fixture style used by that file.
/// </summary>
public class GenericArityCollisionToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _registryPath;
    private readonly IServiceProvider _serviceProvider;

    public GenericArityCollisionToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"GenericArity_{Guid.NewGuid():N}");
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

    private string LoadPair(string leftSource, string rightSource)
    {
        // Keep both assemblies in one directory so each type system resolves sibling references.
        var left = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "ArityLeft", leftSource);
        var right = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "ArityRight", rightSource);

        LoadAssemblyTool.LoadAssembly(assemblyPath: left, contextAlias: "arity-left", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(assemblyPath: right, contextAlias: "arity-right", rebuildIndex: false, makeCurrent: false);

        return right;
    }

    private const string TwinSourceV1 =
        """
        namespace Arity
        {
            public interface IValue
            {
                void Apply(int value);
            }

            public interface IValue<T>
            {
                void Apply(T value);
            }

            public class Impl
            {
                public void NonGeneric()
                {
                }

                public void Generic<T>(T value)
                {
                }
            }
        }
        """;

    private const string TwinSourceV2 =
        """
        namespace Arity
        {
            public interface IValue
            {
                void Apply(int value);
                void Apply(string value);
            }

            public interface IValue<T>
            {
                void Apply(T value);
                void Apply(T first, T second);
            }

            public class Impl
            {
                public void NonGeneric()
                {
                }

                public void Added()
                {
                }

                public void Generic<T>(T value)
                {
                }
            }
        }
        """;

    [Fact]
    public void CompareContexts_GenericAndNonGenericShareFullName_ReportsBothShapes()
    {
        LoadPair(TwinSourceV1, TwinSourceV2);

        var response = JsonSerializer.Deserialize<JsonElement>(CompareContextsTool.CompareContexts("arity-left", "arity-right"));
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var summary = data.GetProperty("summary");

        // IValue, IValue`1, Impl exist on both sides and all three changed.
        Assert.Equal(0, summary.GetProperty("addedTypes").GetInt32());
        Assert.Equal(0, summary.GetProperty("removedTypes").GetInt32());
        Assert.Equal(3, summary.GetProperty("changedTypes").GetInt32());

        var items = data.GetProperty("items");
        var nonGeneric = items.EnumerateArray()
            .Single(item => item.GetProperty("typeName").GetString() == "Arity.IValue");
        var generic = items.EnumerateArray()
            .Single(item => item.GetProperty("typeName").GetString() == "Arity.IValue`1");

        Assert.Equal("changed", nonGeneric.GetProperty("status").GetString());
        Assert.Equal("changed", generic.GetProperty("status").GetString());

        // Overloads of the same name are folded into one surface entry, so the extra Apply(...)
        // overload surfaces as a changed member rather than an added one.
        Assert.Equal(0, nonGeneric.GetProperty("memberDelta").GetProperty("addedOrRemovedMembers").GetInt32());
        Assert.Equal(1, nonGeneric.GetProperty("memberDelta").GetProperty("changedMembers").GetInt32());
        Assert.Equal(0, generic.GetProperty("memberDelta").GetProperty("addedOrRemovedMembers").GetInt32());
        Assert.Equal(1, generic.GetProperty("memberDelta").GetProperty("changedMembers").GetInt32());

        // Two distinct types, not one type reported twice.
        Assert.Equal(3, items.GetArrayLength());
    }

    [Fact]
    public void CompareSymbols_PlainFullName_ResolvesNonGenericShape()
    {
        LoadPair(TwinSourceV1, TwinSourceV2);

        var response = JsonSerializer.Deserialize<JsonElement>(CompareSymbolsTool.CompareSymbols("arity-left", "arity-right", "Arity.IValue"));
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.GetProperty("leftExists").GetBoolean());
        Assert.True(data.GetProperty("rightExists").GetBoolean());

        // The reflection name matches exactly one type, so the lookup is unambiguous.
        Assert.False(data.TryGetProperty("resolution", out _));

        // Apply(string) exists only on the right, i.e. we diffed the non-generic interface. Overloads
        // share one surface entry, so the new overload shows up as a changed member signature.
        var changed = data.GetProperty("changedMembers").EnumerateArray().Single().GetProperty("rightSignature").GetString()!;
        Assert.Contains("System.String value", changed, StringComparison.Ordinal);
        Assert.DoesNotContain("T value", changed, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareSymbols_SimpleName_MatchesArityAndReportsResolution()
    {
        LoadPair(TwinSourceV1, TwinSourceV2);

        var response = JsonSerializer.Deserialize<JsonElement>(CompareSymbolsTool.CompareSymbols("arity-left", "arity-right", "IValue"));
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var resolution = data.GetProperty("resolution").GetString();
        Assert.NotNull(resolution);
        Assert.Contains("matched 2 type(s) on the left", resolution!, StringComparison.Ordinal);
        Assert.Contains("and 2 on the right", resolution!, StringComparison.Ordinal);
        Assert.Contains("compared Arity.IValue vs Arity.IValue", resolution!, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareSymbols_ReflectionName_PinsGenericShape()
    {
        LoadPair(TwinSourceV1, TwinSourceV2);

        var response = JsonSerializer.Deserialize<JsonElement>(CompareSymbolsTool.CompareSymbols("arity-left", "arity-right", "Arity.IValue`1"));
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.GetProperty("leftExists").GetBoolean());
        Assert.True(data.GetProperty("rightExists").GetBoolean());
        Assert.False(data.TryGetProperty("resolution", out _));

        // "void Apply(T first, T second)" exists only on the right, i.e. we diffed the generic interface.
        var changed = data.GetProperty("changedMembers").EnumerateArray().Single().GetProperty("rightSignature").GetString()!;
        Assert.Contains("T first, T second", changed, StringComparison.Ordinal);
        Assert.DoesNotContain("System.String", changed, StringComparison.Ordinal);
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

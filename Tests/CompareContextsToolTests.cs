using System.Text.Json;
using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class CompareContextsToolTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _registryPath;
    private readonly IServiceProvider _serviceProvider;

    public CompareContextsToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"CompareContexts_{Guid.NewGuid():N}");
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
    public void CompareContexts_ReturnsStructuredTypeDiffSummary()
    {
        var leftAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "ContextLeft",
            """
            namespace VersionedApi
            {
                public class Unchanged
                {
                    public void Ping()
                    {
                    }
                }

                public class Changed
                {
                    public int Value;

                    public void Run()
                    {
                    }
                }

                public class Removed
                {
                }
            }

            namespace VersionedApi.Extra
            {
                public class LeftOnly
                {
                }
            }
            """);

        var rightAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "ContextRight",
            """
            namespace VersionedApi
            {
                public class Unchanged
                {
                    public void Ping()
                    {
                    }
                }

                public class Changed
                {
                    public string Value = "";

                    public void Run()
                    {
                    }

                    public void Added()
                    {
                    }
                }

                public class Added
                {
                }
            }

            namespace VersionedApi.Extra
            {
                public class RightOnly
                {
                }
            }
            """);

        LoadAssemblyTool.LoadAssembly(assemblyPath: leftAssembly, contextAlias: "left", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(assemblyPath: rightAssembly, contextAlias: "right", rebuildIndex: false, makeCurrent: false);

        var result = CompareContextsTool.CompareContexts("left", "right");
        var response = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal("surface", data.GetProperty("compareMode").GetString());
        Assert.Equal("left", data.GetProperty("leftContextAlias").GetString());
        Assert.Equal("right", data.GetProperty("rightContextAlias").GetString());
        Assert.False(data.GetProperty("includeUnchanged").GetBoolean());

        var summary = data.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("addedTypes").GetInt32());
        Assert.Equal(2, summary.GetProperty("removedTypes").GetInt32());
        Assert.Equal(1, summary.GetProperty("changedTypes").GetInt32());
        Assert.Equal(1, summary.GetProperty("unchangedTypes").GetInt32());

        var items = data.GetProperty("items");
        Assert.Equal(5, items.GetArrayLength());
        Assert.Contains(items.EnumerateArray(), item => item.GetProperty("status").GetString() == "added" && item.GetProperty("typeName").GetString() == "VersionedApi.Added");
        Assert.Contains(items.EnumerateArray(), item => item.GetProperty("status").GetString() == "removed" && item.GetProperty("typeName").GetString() == "VersionedApi.Removed");

        var changed = items.EnumerateArray().First(item => item.GetProperty("status").GetString() == "changed");
        Assert.Equal("VersionedApi.Changed", changed.GetProperty("typeName").GetString());
        Assert.Equal(1, changed.GetProperty("memberDelta").GetProperty("addedOrRemovedMembers").GetInt32());
        Assert.Equal(1, changed.GetProperty("memberDelta").GetProperty("changedMembers").GetInt32());
    }

    [Fact]
    public void CompareContexts_WithNamespaceFilterAndIncludeUnchanged_ScopesResults()
    {
        var leftAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "NamespaceLeft",
            """
            namespace VersionedApi
            {
                public class Stable
                {
                }

                public class Removed
                {
                }
            }

            namespace VersionedApi.Inner
            {
                public class DeepLeft
                {
                }
            }

            namespace OtherApi
            {
                public class OtherLeft
                {
                }
            }
            """);

        var rightAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "NamespaceRight",
            """
            namespace VersionedApi
            {
                public class Stable
                {
                }

                public class Added
                {
                }
            }

            namespace VersionedApi.Inner
            {
                public class DeepRight
                {
                }
            }

            namespace OtherApi
            {
                public class OtherRight
                {
                }
            }
            """);

        LoadAssemblyTool.LoadAssembly(assemblyPath: leftAssembly, contextAlias: "left", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(assemblyPath: rightAssembly, contextAlias: "right", rebuildIndex: false, makeCurrent: false);

        var result = CompareContextsTool.CompareContexts(
            "left",
            "right",
            namespaceFilter: "VersionedApi",
            deep: false,
            includeUnchanged: true);

        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal("VersionedApi", data.GetProperty("namespaceFilter").GetString());
        Assert.True(data.GetProperty("includeUnchanged").GetBoolean());

        var summary = data.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("addedTypes").GetInt32());
        Assert.Equal(1, summary.GetProperty("removedTypes").GetInt32());
        Assert.Equal(0, summary.GetProperty("changedTypes").GetInt32());
        Assert.Equal(1, summary.GetProperty("unchangedTypes").GetInt32());

        var items = data.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.All(items.EnumerateArray(), item => Assert.Equal("VersionedApi", item.GetProperty("namespace").GetString()));
        Assert.Contains(items.EnumerateArray(), item => item.GetProperty("status").GetString() == "unchanged" && item.GetProperty("typeName").GetString() == "VersionedApi.Stable");
    }

    /// <summary>
    /// ITypeDefinition.FullName drops generic arity, so "N.IFoo" and "N.IFoo&lt;T&gt;" collide.
    /// Keying the diff on FullName used to throw "An item with the same key has already been added",
    /// which made compare_contexts unusable on assemblies declaring both forms.
    /// </summary>
    [Fact]
    public void CompareContexts_WithGenericAndNonGenericTwin_DoesNotCollide()
    {
        var leftAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "GenericLeft",
            """
            namespace Arity
            {
                public interface IParameterTable
                {
                    int Add();
                }

                public interface IParameterTable<T>
                {
                    void SetValue(T value);
                }
            }
            """);

        var rightAssembly = TemporaryAssemblyBuilder.BuildLibrary(_tempDir, "GenericRight",
            """
            namespace Arity
            {
                public interface IParameterTable
                {
                    int Add();
                    void Remove();
                }

                public interface IParameterTable<T>
                {
                    void SetValue(T value);
                }
            }
            """);

        LoadAssemblyTool.LoadAssembly(assemblyPath: leftAssembly, contextAlias: "left", rebuildIndex: false);
        LoadAssemblyTool.LoadAssembly(assemblyPath: rightAssembly, contextAlias: "right", rebuildIndex: false, makeCurrent: false);

        var result = CompareContextsTool.CompareContexts("left", "right", includeUnchanged: true);
        var response = JsonSerializer.Deserialize<JsonElement>(result);

        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var summary = data.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("totalLeftTypes").GetInt32());
        Assert.Equal(2, summary.GetProperty("totalRightTypes").GetInt32());

        // Both twins survive as separate rows: one changed, one unchanged.
        Assert.Equal(1, summary.GetProperty("changedTypes").GetInt32());
        Assert.Equal(1, summary.GetProperty("unchangedTypes").GetInt32());
        Assert.False(data.TryGetProperty("collisionReport", out _));

        var items = data.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Contains(items.EnumerateArray(), item => item.GetProperty("typeName").GetString() == "Arity.IParameterTable" && item.GetProperty("status").GetString() == "changed");
        Assert.Contains(items.EnumerateArray(), item => item.GetProperty("typeName").GetString() == "Arity.IParameterTable`1" && item.GetProperty("status").GetString() == "unchanged");
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

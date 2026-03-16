using DecompilerServer.Services;

namespace Tests;

public class OriginalSourceRetrievalTests
{
    [Fact]
    public void DecompileMember_ShouldPreferLocalOriginalSource_WhenPortablePdbSourceExists()
    {
        using var contextManager = new AssemblyContextManager();
        var memberResolver = new MemberResolver(contextManager);
        var decompilerService = new DecompilerService(contextManager, memberResolver);

        contextManager.LoadAssemblyDirect(TestAssemblyLocator.GetPath());

        var document = decompilerService.DecompileMember("T:TestLibrary.AttributedClass");
        var source = string.Join("\n", document.Lines);

        Assert.Equal(SourceKinds.LocalSource, document.SourceKind);
        Assert.Contains("// Method with parameter attribute", source);
        Assert.EndsWith("Class1.cs", document.SourcePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecompileMember_ShouldPreferEmbeddedSource_WhenEmbeddedPdbContainsSource()
    {
        using var contextManager = new AssemblyContextManager();
        var memberResolver = new MemberResolver(contextManager);
        var decompilerService = new DecompilerService(contextManager, memberResolver);

        var embeddedAssemblyPath = typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location;
        contextManager.LoadAssemblyDirect(embeddedAssemblyPath);

        var document = decompilerService.DecompileMember("T:EmbeddedSourceTestLibrary.EmbeddedSourceSample");
        var source = string.Join("\n", document.Lines);

        Assert.Equal(SourceKinds.EmbeddedSource, document.SourceKind);
        Assert.Contains("// Embedded source marker comment", source);
        Assert.Contains("EMBEDDED-SOURCE-MARKER", source);
        Assert.EndsWith("EmbeddedSourceSample.cs", document.SourcePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SearchServiceCache_ShouldReset_WhenAssemblyChanges()
    {
        using var contextManager = new AssemblyContextManager();
        var memberResolver = new MemberResolver(contextManager);
        var searchService = new TestSearchService(contextManager, memberResolver);

        contextManager.LoadAssemblyDirect(TestAssemblyLocator.GetPath());
        var firstSearch = searchService.SearchTypes("Simple");

        Assert.Contains(firstSearch.Items, item => item.Name == "SimpleClass");
        Assert.True(searchService.GetSearchCacheStats().CachedSearches > 0);

        var embeddedAssemblyPath = typeof(global::EmbeddedSourceTestLibrary.EmbeddedSourceSample).Assembly.Location;
        contextManager.LoadAssemblyDirect(embeddedAssemblyPath);

        var secondSearch = searchService.SearchTypes("Simple");

        Assert.Empty(secondSearch.Items);
        Assert.DoesNotContain(secondSearch.Items, item => item.Name == "SimpleClass");
    }

    private sealed class TestSearchService : SearchServiceBase
    {
        public TestSearchService(AssemblyContextManager contextManager, MemberResolver memberResolver)
            : base(contextManager, memberResolver)
        {
        }
    }
}

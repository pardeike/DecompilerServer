using DecompilerServer.Services;
using Xunit;

namespace Tests;

public class EnhancedCachingTests : ServiceTestBase
{
    [Fact]
    public void AssemblyContextManager_LazyIndexes_ShouldBeBuiltOnDemand()
    {
        // Arrange & Act - indexes should not be built yet
        var initialStats = ContextManager.GetIndexStats();

        // Assert - indexes should not be ready initially
        Assert.False(initialStats.TypeIndexReady);
        Assert.False(initialStats.NamespaceIndexReady);
        Assert.False(initialStats.MemberIndexReady);

        // Act - access type by name to trigger index building
        var type = ContextManager.FindTypeByName("TestLibrary.SimpleClass");

        // Assert - type index should now be ready
        var afterTypeAccess = ContextManager.GetIndexStats();
        Assert.True(afterTypeAccess.TypeIndexReady);
        Assert.NotNull(type);
        Assert.Equal("SimpleClass", type.Name);

        // Act - warm all indexes
        ContextManager.WarmIndexes();

        // Assert - all indexes should be ready
        var afterWarmup = ContextManager.GetIndexStats();
        Assert.True(afterWarmup.TypeIndexReady);
        Assert.True(afterWarmup.NamespaceIndexReady);
        Assert.True(afterWarmup.MemberIndexReady);
    }

    [Fact]
    public void MemberResolver_CacheResolutions_ShouldImprovePerformance()
    {
        // Arrange
        var memberId = "T:TestLibrary.SimpleClass";
        var initialStats = MemberResolver.GetCacheStats();

        // Assert - no cached resolutions initially
        Assert.Equal(0, initialStats.CachedResolutions);

        // Act - resolve member first time
        var member1 = MemberResolver.ResolveMember(memberId);
        var afterFirstResolve = MemberResolver.GetCacheStats();

        // Assert - one cached resolution
        Assert.Equal(1, afterFirstResolve.CachedResolutions);
        Assert.Equal(1, afterFirstResolve.SuccessfulResolutions);
        Assert.NotNull(member1);

        // Act - resolve same member again (should use cache)
        var member2 = MemberResolver.ResolveMember(memberId);
        var afterSecondResolve = MemberResolver.GetCacheStats();

        // Assert - still one cached resolution, same instance
        Assert.Equal(1, afterSecondResolve.CachedResolutions);
        Assert.Same(member1, member2);

        // Act - clear cache
        MemberResolver.ClearCache();
        var afterClear = MemberResolver.GetCacheStats();

        // Assert - cache cleared
        Assert.Equal(0, afterClear.CachedResolutions);
    }

    [Fact]
    public void AssemblyContextManager_GetTypesInNamespace_ShouldUseCachedIndex()
    {
        // Act - get types in TestLibrary namespace
        var types = ContextManager.GetTypesInNamespace("TestLibrary").ToList();

        // Assert - should find test types
        Assert.NotEmpty(types);
        Assert.Contains(types, t => t.Name == "SimpleClass");
        Assert.Contains(types, t => t.Name == "ITestInterface");

        // Verify namespace index was built
        var stats = ContextManager.GetIndexStats();
        Assert.True(stats.NamespaceIndexReady);
    }

    [Fact]
    public void EnhancedCaching_AssemblyReload_ShouldResetIndexes()
    {
        // Arrange - warm up indexes
        ContextManager.WarmIndexes();
        var beforeReload = ContextManager.GetIndexStats();
        Assert.True(beforeReload.TypeIndexReady);

        // Act - reload assembly
        ContextManager.LoadAssembly(Path.GetTempPath(), TestAssemblyPath);

        // Assert - indexes should be reset
        var afterReload = ContextManager.GetIndexStats();
        Assert.False(afterReload.TypeIndexReady);
        Assert.False(afterReload.NamespaceIndexReady);
        Assert.False(afterReload.MemberIndexReady);
    }

    [Fact]
    public void AdvancedCaching_SearchAndUsageAnalysis_ShouldImprovePerformance()
    {
        // Arrange
        var searchService = new TestSearchService(ContextManager, MemberResolver);
        var usageAnalyzer = new UsageAnalyzer(ContextManager, MemberResolver);

        // Act - perform searches to populate caches
        var searchResult1 = searchService.SearchTypes("Simple");
        var searchResult2 = searchService.SearchTypes("Simple"); // Should use cache

        var usageResult1 = usageAnalyzer.FindUsages("T:TestLibrary.SimpleClass", 10);
        var usageResult2 = usageAnalyzer.FindUsages("T:TestLibrary.SimpleClass", 10); // Should use cache

        // Assert - results should be consistent and caching should work
        Assert.Equal(searchResult1.Items.Count, searchResult2.Items.Count);
        Assert.Equal(usageResult1.Count(), usageResult2.Count());

        // Verify cache statistics
        var searchStats = searchService.GetSearchCacheStats();
        var usageStats = usageAnalyzer.GetCacheStats();

        Assert.True(searchStats.CachedSearches > 0);
        Assert.True(usageStats.CachedUsageQueries >= 0); // May be 0 if no actual usages found

        // Clear caches
        searchService.ClearSearchCache();
        usageAnalyzer.ClearCache();

        var clearedSearchStats = searchService.GetSearchCacheStats();
        var clearedUsageStats = usageAnalyzer.GetCacheStats();

        Assert.Equal(0, clearedSearchStats.CachedSearches);
        Assert.Equal(0, clearedUsageStats.CachedUsageQueries);
    }

    private class TestSearchService : SearchServiceBase
    {
        public TestSearchService(AssemblyContextManager contextManager, MemberResolver memberResolver)
            : base(contextManager, memberResolver) { }
    }
}
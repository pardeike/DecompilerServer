using DecompilerServer.Services;

namespace Tests;

public class ServiceIntegrationTests : ServiceTestBase
{
    [Fact]
    public void DecompilerService_BatchDecompile_ShouldReturnMultipleDocuments()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);
        var memberIds = new[]
        {
            "T:TestLibrary.SimpleClass",
            "T:TestLibrary.ITestInterface",
            "T:TestLibrary.DerivedClass"
        };

        // Act
        var documents = decompilerService.BatchDecompile(memberIds).ToList();

        // Assert
        Assert.Equal(3, documents.Count);
        Assert.All(documents, doc => Assert.NotNull(doc));
        Assert.All(documents, doc => Assert.True(doc.TotalLines > 0));

        var memberIdList = documents.Select(d => d.MemberId).ToList();
        Assert.Contains("T:TestLibrary.SimpleClass", memberIdList);
        Assert.Contains("T:TestLibrary.ITestInterface", memberIdList);
        Assert.Contains("T:TestLibrary.DerivedClass", memberIdList);
    }

    [Fact]
    public void DecompilerService_GetSourceSlice_ShouldReturnCorrectSlice()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);
        decompilerService.DecompileMember("T:TestLibrary.SimpleClass"); // Ensure cached

        // Act
        var slice = decompilerService.GetSourceSlice("T:TestLibrary.SimpleClass", 1, 5);

        // Assert
        Assert.NotNull(slice);
        Assert.Equal("T:TestLibrary.SimpleClass", slice.MemberId);
        Assert.Equal(1, slice.StartLine);
        Assert.Equal(5, slice.EndLine);
        Assert.NotNull(slice.Code);
        Assert.NotEmpty(slice.Code);
    }

    [Fact]
    public void DecompilerService_CacheStats_ShouldReflectCachedItems()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);

        // Act - Decompile a few items to populate cache
        decompilerService.DecompileMember("T:TestLibrary.SimpleClass");
        decompilerService.DecompileMember("T:TestLibrary.ITestInterface");
        var stats = decompilerService.GetCacheStats();

        // Assert
        Assert.NotNull(stats);
        Assert.True(stats.SourceDocuments >= 2);
        Assert.True(stats.TotalMemoryEstimate > 0);
    }

    [Fact]
    public void MemberResolver_NormalizeMemberId_ShouldReturnConsistentFormat()
    {
        // Act
        var normalized1 = MemberResolver.NormalizeMemberId("T:TestLibrary.SimpleClass");
        var normalized2 = MemberResolver.NormalizeMemberId("TestLibrary.SimpleClass");

        // Assert
        Assert.NotNull(normalized1);
        Assert.NotNull(normalized2);
        // Both should start with the prefix
        Assert.StartsWith("T:", normalized1);
    }

    [Fact]
    public void MemberResolver_GenerateMemberId_ShouldCreateValidId()
    {
        // Arrange
        var types = ContextManager.GetAllTypes().ToList();
        var simpleClassType = types.FirstOrDefault(t => t.Name == "SimpleClass");
        Assert.NotNull(simpleClassType);

        // Act
        var memberId = MemberResolver.GenerateMemberId(simpleClassType);

        // Assert
        Assert.NotNull(memberId);
        Assert.StartsWith("T:", memberId);
        Assert.Contains("SimpleClass", memberId);

        // Should be able to resolve back
        var resolved = MemberResolver.ResolveMember(memberId);
        Assert.NotNull(resolved);
    }

    [Fact]
    public void AssemblyContextManager_GetAllTypes_ShouldContainTestTypes()
    {
        // Act
        var types = ContextManager.GetAllTypes().ToList();
        var typeNames = types.Select(t => t.Name).ToList();

        // Assert
        Assert.NotEmpty(types);
        Assert.Contains("SimpleClass", typeNames);
        Assert.Contains("DerivedClass", typeNames);
        Assert.Contains("ITestInterface", typeNames);
        Assert.Contains("TestEnum", typeNames);
        Assert.Contains("OuterClass", typeNames);

        // Check if we have at least one generic class (name may vary)
        var hasGenericClass = typeNames.Any(name => name.StartsWith("GenericClass"));
        Assert.True(hasGenericClass, "Should contain a generic class");
    }

    [Fact]
    public void InheritanceAnalyzer_FindBaseTypes_ShouldFindCorrectInheritance()
    {
        // Arrange
        var analyzer = new InheritanceAnalyzer(ContextManager, MemberResolver);

        // Act
        var baseTypes = analyzer.FindBaseTypes("T:TestLibrary.DerivedClass", 10).ToList();

        // Assert
        Assert.NotEmpty(baseTypes);

        // Should contain BaseClass in the inheritance chain
        var hasBaseClass = baseTypes.Any(bt => bt.Name?.Contains("BaseClass") == true);
        Assert.True(hasBaseClass, "Should find BaseClass in inheritance chain");
    }

    [Fact]
    public void UsageAnalyzer_FindUsages_ShouldNotThrowForValidMember()
    {
        // Arrange
        var analyzer = new UsageAnalyzer(ContextManager, MemberResolver);

        // Act & Assert - Should not throw
        var usages = analyzer.FindUsages("T:TestLibrary.SimpleClass", 50).ToList();

        // Note: May be empty if no usages found, but should not throw
        Assert.NotNull(usages);
    }

    [Fact]
    public void SearchServiceBase_SearchTypes_ShouldFindTestTypes()
    {
        // Arrange
        var searchService = new TestSearchService(ContextManager, MemberResolver);

        // Act
        var results = searchService.SearchTypes("Simple", regex: false, limit: 10);

        // Assert
        Assert.NotNull(results);
        Assert.NotNull(results.Items);

        // Should find SimpleClass
        var hasSimpleClass = results.Items.Any(item => item.Name?.Contains("SimpleClass") == true);
        Assert.True(hasSimpleClass, "Should find SimpleClass in search results");
    }

    [Fact]
    public void UsageAnalyzer_FindCallees_ShouldDetectBaseMethodCall()
    {
        var analyzer = new UsageAnalyzer(ContextManager, MemberResolver);
        var callees = analyzer.FindCallees("M:TestLibrary.DerivedClass.VirtualMethod").ToList();
        Assert.Contains(callees, c => c.InMember == "M:TestLibrary.BaseClass.VirtualMethod");
    }

    [Fact]
    public void UsageAnalyzer_FindStringLiterals_ShouldFindLiteral()
    {
        var analyzer = new UsageAnalyzer(ContextManager, MemberResolver);
        var literals = analyzer.FindStringLiterals("Simple method called").ToList();
        Assert.Contains(literals, l => l.Value == "Simple method called");
    }

    private class TestSearchService : SearchServiceBase
    {
        public TestSearchService(AssemblyContextManager contextManager, MemberResolver memberResolver)
            : base(contextManager, memberResolver)
        {
        }
    }
}
using DecompilerServer.Services;

namespace Tests;

public class ServiceIntegrationTests : ServiceTestBase
{
    [Fact]
    public void DecompilerService_BatchDecompile_ShouldReturnMultipleDocuments()
    {
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);
        var memberIds = new[]
        {
            MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.SimpleClass")!),
            MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.ITestInterface")!),
            MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.DerivedClass")!)
        };

        // Act
        var documents = decompilerService.BatchDecompile(memberIds).ToList();

        // Assert
        Assert.Equal(3, documents.Count);
        Assert.All(documents, doc => Assert.NotNull(doc));
        Assert.All(documents, doc => Assert.True(doc.TotalLines > 0));

        var memberIdList = documents.Select(d => d.MemberId).ToList();
        Assert.All(memberIds, id => Assert.Contains(id, memberIdList));
    }

    [Fact]
    public void DecompilerService_GetSourceSlice_ShouldReturnCorrectSlice()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);
        var memberId = MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.SimpleClass")!);
        decompilerService.DecompileMember(memberId); // Ensure cached

        var slice = decompilerService.GetSourceSlice(memberId, 1, 5);

        Assert.NotNull(slice);
        Assert.Equal(memberId, slice.MemberId);
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
        var simpleId = MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.SimpleClass")!);
        var interfaceId = MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.ITestInterface")!);
        decompilerService.DecompileMember(simpleId);
        decompilerService.DecompileMember(interfaceId);
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
        var memberId = MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.SimpleClass")!);
        var normalized = MemberResolver.NormalizeMemberId(memberId);

        Assert.Equal(memberId, normalized);
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
        Assert.Matches(@"^[0-9a-f]{32}:[0-9a-f]{8}:T$", memberId);

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
        var memberId = MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.DerivedClass")!);
        var baseTypes = analyzer.FindBaseTypes(memberId, 10).ToList();

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
        var memberId = MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.SimpleClass")!);
        var usages = analyzer.FindUsages(memberId, 50).ToList();

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
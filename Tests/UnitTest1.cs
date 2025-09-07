using DecompilerServer.Services;

namespace Tests;

public class SimpleServiceTests : ServiceTestBase
{
    [Fact]
    public void AssemblyContextManager_WhenLoaded_ShouldBeValid()
    {
        // Assert
        Assert.True(ContextManager.IsLoaded);
        Assert.True(ContextManager.TypeCount > 0);
        Assert.True(ContextManager.NamespaceCount > 0);
    }

    [Fact]
    public void MemberResolver_ResolveSimpleClass_ShouldReturnValidType()
    {
        var memberId = MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.SimpleClass")!);
        var entity = MemberResolver.ResolveMember(memberId);

        // Assert
        Assert.NotNull(entity);
        Assert.Contains("SimpleClass", entity.Name);
    }

    [Fact]
    public void MemberResolver_IsValidMemberId_ShouldValidateCorrectly()
    {
        var memberId = MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.SimpleClass")!);
        Assert.True(MemberResolver.IsValidMemberId(memberId));
        Assert.False(MemberResolver.IsValidMemberId(""));
        Assert.False(MemberResolver.IsValidMemberId("InvalidFormat"));
    }

    [Fact]
    public void DecompilerService_DecompileMember_ShouldReturnValidSource()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);

        var memberId = MemberResolver.GenerateMemberId(ContextManager.FindTypeByName("TestLibrary.SimpleClass")!);
        var document = decompilerService.DecompileMember(memberId);

        // Assert
        Assert.NotNull(document);
        Assert.Equal(memberId, document.MemberId);
        Assert.Equal("C#", document.Language);
        Assert.True(document.TotalLines > 0);
        Assert.NotNull(document.Lines);
        Assert.True(document.Lines.Length > 0);

        var source = string.Join("\n", document.Lines);
        Assert.Contains("SimpleClass", source);
    }

    [Fact]
    public void ResponseFormatter_Success_ShouldReturnValidJson()
    {
        // Act
        var response = ResponseFormatter.Success("test data");

        // Assert
        Assert.NotNull(response);
        Assert.Contains("status", response);
        Assert.Contains("ok", response);
        Assert.Contains("test data", response);
    }

    [Fact]
    public void ResponseFormatter_Error_ShouldReturnErrorJson()
    {
        // Act
        var response = ResponseFormatter.Error("test error");

        // Assert
        Assert.NotNull(response);
        Assert.Contains("status", response);
        Assert.Contains("error", response);
        Assert.Contains("test error", response);
    }

    [Fact]
    public void UsageAnalyzer_CanBeCreated_ShouldNotThrow()
    {
        // Act & Assert - should not throw
        var analyzer = new UsageAnalyzer(ContextManager, MemberResolver);
        Assert.NotNull(analyzer);
    }

    [Fact]
    public void InheritanceAnalyzer_CanBeCreated_ShouldNotThrow()
    {
        // Act & Assert - should not throw
        var analyzer = new InheritanceAnalyzer(ContextManager, MemberResolver);
        Assert.NotNull(analyzer);
    }

    [Fact]
    public void SearchServiceBase_CanBeSubclassed_ShouldNotThrow()
    {
        // Act & Assert - should not throw
        var searchService = new TestSearchService(ContextManager, MemberResolver);
        Assert.NotNull(searchService);
    }

    private class TestSearchService : SearchServiceBase
    {
        public TestSearchService(AssemblyContextManager contextManager, MemberResolver memberResolver)
            : base(contextManager, memberResolver)
        {
        }
    }
}
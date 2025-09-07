using DecompilerServer.Services;
using ICSharpCode.Decompiler.TypeSystem;
using System.Reflection.Metadata.Ecma335;

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
        // Act
        var entity = MemberResolver.ResolveMember("T:TestLibrary.SimpleClass");

        // Assert
        Assert.NotNull(entity);
        Assert.Contains("SimpleClass", entity.Name);
    }

    [Fact]
    public void MemberResolver_IsValidMemberId_ShouldValidateCorrectly()
    {
        // Act & Assert
        Assert.True(MemberResolver.IsValidMemberId("T:TestLibrary.SimpleClass"));
        Assert.False(MemberResolver.IsValidMemberId(""));
        Assert.False(MemberResolver.IsValidMemberId("InvalidFormat"));
    }

    [Fact]
    public void MemberResolver_ResolveHexToken_ShouldReturnMember()
    {
        var method = (IMethod)MemberResolver.ResolveMember("M:TestLibrary.SimpleClass.SimpleMethod")!;
        var token = $"0x{MetadataTokens.GetToken(method.MetadataToken):X8}";

        var resolved = MemberResolver.ResolveMember(token);

        Assert.NotNull(resolved);
        Assert.Equal(method, resolved);
    }

    [Fact]
    public void MemberResolver_ResolveDecimalToken_ShouldReturnMember()
    {
        var field = (IField)MemberResolver.ResolveMember("F:TestLibrary.SimpleClass.PublicField")!;
        var token = MetadataTokens.GetToken(field.MetadataToken).ToString();

        var resolved = MemberResolver.ResolveMember(token);

        Assert.NotNull(resolved);
        Assert.Equal(field, resolved);
    }

    [Fact]
    public void DecompilerService_DecompileMember_ShouldReturnValidSource()
    {
        // Arrange
        var decompilerService = new DecompilerService(ContextManager, MemberResolver);

        // Act
        var document = decompilerService.DecompileMember("T:TestLibrary.SimpleClass");

        // Assert
        Assert.NotNull(document);
        Assert.Equal("T:TestLibrary.SimpleClass", document.MemberId);
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
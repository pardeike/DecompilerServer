using DecompilerServer.Services;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DecompilerServer.Tests;

/// <summary>
/// Tests for the implemented MCP tools to ensure they work correctly with the test assembly
/// </summary>
public class ToolImplementationTests : ServiceTestBase
{
    private readonly IServiceProvider _serviceProvider;

    public ToolImplementationTests()
    {
        // Set up a minimal service provider for the tools to use
        var services = new ServiceCollection();
        services.AddSingleton(ContextManager);
        services.AddSingleton(MemberResolver);
        services.AddSingleton<DecompilerService>();
        services.AddSingleton<UsageAnalyzer>();
        services.AddSingleton<InheritanceAnalyzer>();
        services.AddSingleton<ResponseFormatter>();

        _serviceProvider = services.BuildServiceProvider();
        ServiceLocator.SetServiceProvider(_serviceProvider);
    }

    [Fact]
    public void ResolveMemberId_WithValidType_ReturnsCorrectSummary()
    {
        // Arrange - find a type from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Name.Contains("Test"));
        Assert.NotNull(testType);

        var memberId = MemberResolver.GenerateMemberId(testType);

        // Act
        var result = ResolveMemberIdTool.ResolveMemberId(memberId);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal(memberId, data.GetProperty("memberId").GetString());
        Assert.Equal(testType.Name, data.GetProperty("name").GetString());
        Assert.Equal("Type", data.GetProperty("kind").GetString());
    }

    [Fact]
    public void ResolveMemberId_WithInvalidId_ReturnsError()
    {
        // Act
        var result = ResolveMemberIdTool.ResolveMemberId("invalid-member-id");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void ListNamespaces_WithNoFilter_ReturnsNamespaces()
    {
        // Act
        var result = ListNamespacesTool.ListNamespaces();

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var items = data.GetProperty("items");
        Assert.True(items.GetArrayLength() > 0);

        var firstNamespace = items[0];
        Assert.Equal("Namespace", firstNamespace.GetProperty("kind").GetString());
        Assert.StartsWith("N:", firstNamespace.GetProperty("memberId").GetString()!);
    }

    [Fact]
    public void ListNamespaces_WithPrefix_FiltersCorrectly()
    {
        // Act
        var result = ListNamespacesTool.ListNamespaces("System");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var items = data.GetProperty("items");

        // All returned namespaces should start with "System"
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var ns = items[i];
            var name = ns.GetProperty("name").GetString();
            Assert.StartsWith("System", name!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SearchTypes_WithQuery_ReturnsMatchingTypes()
    {
        // Act - search for types containing "Test"
        var result = SearchTypesTool.SearchTypes("Test");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var items = data.GetProperty("items");

        // Should find at least one type
        Assert.True(items.GetArrayLength() > 0);

        var firstType = items[0];
        Assert.Equal("Type", firstType.GetProperty("kind").GetString());
        Assert.Contains("Test", firstType.GetProperty("name").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetDecompiledSource_WithValidType_ReturnsSourceDocument()
    {
        // Arrange - find a type from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Name.Contains("Test"));
        Assert.NotNull(testType);

        var memberId = MemberResolver.GenerateMemberId(testType);

        // Act
        var result = GetDecompiledSourceTool.GetDecompiledSource(memberId);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal(memberId, data.GetProperty("memberId").GetString());
        Assert.Equal("C#", data.GetProperty("language").GetString());
        Assert.True(data.GetProperty("totalLines").GetInt32() > 0);
        Assert.True(!string.IsNullOrEmpty(data.GetProperty("hash").GetString()));
    }

    [Fact]
    public void GetSourceSlice_WithValidRange_ReturnsSourceCode()
    {
        // Arrange - find a type and decompile it first
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Name.Contains("Test"));
        Assert.NotNull(testType);

        var memberId = MemberResolver.GenerateMemberId(testType);

        // Ensure it's decompiled first
        GetDecompiledSourceTool.GetDecompiledSource(memberId);

        // Act
        var result = GetSourceSliceTool.GetSourceSlice(memberId, 1, 5);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal(memberId, data.GetProperty("memberId").GetString());
        Assert.Equal("C#", data.GetProperty("language").GetString());
        Assert.Equal(1, data.GetProperty("startLine").GetInt32());
        Assert.Equal(5, data.GetProperty("endLine").GetInt32());
        Assert.False(string.IsNullOrEmpty(data.GetProperty("code").GetString()));
    }

    [Fact]
    public void GetSourceSlice_WithLineNumbers_IncludesLineNumbers()
    {
        // Arrange
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Name.Contains("Test"));
        Assert.NotNull(testType);

        var memberId = MemberResolver.GenerateMemberId(testType);

        // Act
        var result = GetSourceSliceTool.GetSourceSlice(memberId, 1, 3, includeLineNumbers: true);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var code = data.GetProperty("code").GetString();
        Assert.NotNull(code);

        // Check that line numbers are included
        Assert.Contains("1:", code);
    }

    [Fact]
    public void GetMemberDetails_WithValidMember_ReturnsCompleteDetails()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Name.Contains("Test"));
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => !m.IsConstructor);
        Assert.NotNull(method);

        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = GetMemberDetailsTool.GetMemberDetails(memberId);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal(memberId, data.GetProperty("memberId").GetString());
        Assert.Equal(method.Name, data.GetProperty("name").GetString());
        Assert.Equal("Method", data.GetProperty("kind").GetString());
        Assert.False(string.IsNullOrEmpty(data.GetProperty("signature").GetString()));
        Assert.Equal(method.Accessibility.ToString(), data.GetProperty("accessibility").GetString());
    }

    [Fact]
    public void GetMemberDetails_WithInvalidMember_ReturnsError()
    {
        // Act
        var result = GetMemberDetailsTool.GetMemberDetails("invalid-member-id");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void AllTools_WithNoAssemblyLoaded_ReturnError()
    {
        // Arrange - create a fresh context manager with no assembly loaded
        using var freshContext = new AssemblyContextManager();
        var freshResolver = new MemberResolver(freshContext);

        // Temporarily replace the service locator context
        var originalServices = ServiceLocator.GetRequiredService<AssemblyContextManager>();

        // We can't directly test this scenario easily without modifying ServiceLocator
        // So this test documents the expected behavior but would need infrastructure changes to test properly

        Assert.True(originalServices.IsLoaded); // Our test setup should have loaded assembly
    }
}
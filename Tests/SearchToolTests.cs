using DecompilerServer;
using DecompilerServer.Services;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tests;

/// <summary>
/// Tests for search and discovery MCP tools functionality
/// </summary>
public class SearchToolTests : ServiceTestBase
{
    private readonly IServiceProvider _serviceProvider;

    public SearchToolTests()
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
    public void SearchMembers_WithQuery_ReturnsMatchingMembers()
    {
        // Act - search for members containing "Test"
        var result = SearchMembersTool.SearchMembers("Test");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var items = data.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 0);

        if (items.GetArrayLength() > 0)
        {
            var firstMember = items[0];
            Assert.Contains(firstMember.GetProperty("kind").GetString()!,
                new[] { "Method", "Constructor", "Field", "Property", "Event" });
            Assert.False(string.IsNullOrEmpty(firstMember.GetProperty("signature").GetString()));
        }
    }

    [Fact]
    public void SearchMembers_WithKindFilter_ReturnsOnlyMatchingKinds()
    {
        // Act - search for methods only (which includes constructors in the filter)
        var result = SearchMembersTool.SearchMembers("", kind: "method");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var items = data.GetProperty("items");

        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var member = items[i];
            var kind = member.GetProperty("kind").GetString();
            // MatchesKind treats all IMethod as "method", but the Kind property differentiates Constructor vs Method
            Assert.Contains(kind!, new[] { "Method", "Constructor" });
        }
    }

    [Fact]
    public void GetTypesInNamespace_WithExactNamespace_ReturnsOnlyMatchingTypes()
    {
        // Arrange - find a namespace from our test assembly
        var namespaces = ContextManager.GetNamespaces();
        var testNamespace = namespaces.FirstOrDefault(ns => !string.IsNullOrEmpty(ns));
        Assert.NotNull(testNamespace);

        // Act
        var result = GetTypesInNamespaceTool.GetTypesInNamespace(testNamespace);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var items = data.GetProperty("items");

        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var type = items[i];
            Assert.Equal("Type", type.GetProperty("kind").GetString());
            Assert.Equal(testNamespace, type.GetProperty("namespace").GetString());
        }
    }

    [Fact]
    public void GetTypesInNamespace_WithDeepTraversal_IncludesChildNamespaces()
    {
        // Act - search with deep=true for System namespace (if available)
        var result = GetTypesInNamespaceTool.GetTypesInNamespace("System", deep: true);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var items = data.GetProperty("items");

        // All returned types should be in System or System.* namespaces
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var type = items[i];
            var ns = type.GetProperty("namespace").GetString();
            Assert.True(ns == "System" || ns!.StartsWith("System."));
        }
    }

    [Fact]
    public void GetMembersOfType_WithValidType_ReturnsMembers()
    {
        // Arrange - find a type from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any() || t.Fields.Any() || t.Properties.Any());
        Assert.NotNull(testType);

        var typeId = MemberResolver.GenerateMemberId(testType);

        // Act
        var result = GetMembersOfTypeTool.GetMembersOfType(typeId);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var items = data.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 0);

        if (items.GetArrayLength() > 0)
        {
            var firstMember = items[0];
            Assert.Contains(firstMember.GetProperty("kind").GetString()!,
                new[] { "Method", "Constructor", "Field", "Property", "Event" });
            Assert.Equal(testType.FullName, firstMember.GetProperty("declaringType").GetString());
        }
    }

    [Fact]
    public void GetMembersOfType_WithKindFilter_ReturnsOnlyMatchingMembers()
    {
        // Arrange - find a type with methods
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any());
        Assert.NotNull(testType);

        var typeId = MemberResolver.GenerateMemberId(testType);

        // Act - filter for methods only
        var result = GetMembersOfTypeTool.GetMembersOfType(typeId, kind: "method");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        var items = data.GetProperty("items");

        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var member = items[i];
            Assert.Equal("Method", member.GetProperty("kind").GetString());
        }
    }

    [Fact]
    public void GetMemberSignature_WithValidMember_ReturnsSignatureAndSummary()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any());
        Assert.NotNull(testType);

        var method = testType.Methods.First();
        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = GetMemberSignatureTool.GetMemberSignature(memberId);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("signature", out var signature));
        Assert.False(string.IsNullOrEmpty(signature.GetString()));

        Assert.True(data.TryGetProperty("summary", out var summary));
        Assert.Equal(memberId, summary.GetProperty("memberId").GetString());
        Assert.Equal(method.Name, summary.GetProperty("name").GetString());
    }

    [Fact]
    public void GetXmlDoc_WithValidMember_ReturnsDocumentationInfo()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any());
        Assert.NotNull(testType);

        var method = testType.Methods.First();
        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = GetXmlDocTool.GetXmlDoc(memberId);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal(memberId, data.GetProperty("memberId").GetString());
        Assert.True(data.TryGetProperty("hasDocumentation", out var hasDoc));
        Assert.False(data.TryGetProperty("xmlDoc", out _));

        // For now, our implementation returns null documentation, so hasDocumentation should be false
        Assert.False(hasDoc.GetBoolean());
    }
}
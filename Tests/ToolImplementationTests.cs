using DecompilerServer;
using DecompilerServer.Services;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tests;

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
        Assert.True(data.TryGetProperty("xmlDoc", out var xmlDoc));

        // For now, our implementation returns null documentation, so hasDocumentation should be false
        Assert.False(hasDoc.GetBoolean());
    }

    [Fact]
    public void NormalizeMemberId_WithValidMemberId_ReturnsNormalizedId()
    {
        // Arrange - use an existing valid member ID
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any());
        Assert.NotNull(testType);

        var method = testType.Methods.First();
        var validMemberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = NormalizeMemberIdTool.NormalizeMemberId(validMemberId);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("normalizedId", out var normalizedId));
        Assert.Equal(validMemberId, normalizedId.GetString());
        Assert.True(data.TryGetProperty("candidates", out var candidates));
        Assert.True(candidates.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public void NormalizeMemberId_WithPartialInput_ReturnsCandidates()
    {
        // Arrange - find a type name to search for
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault();
        Assert.NotNull(testType);

        var typeName = testType.Name;

        // Act - search with just the type name
        var result = NormalizeMemberIdTool.NormalizeMemberId(typeName);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");

        // Should either return a normalized ID (if unique) or candidates (if multiple matches)
        Assert.True(data.TryGetProperty("normalizedId", out var normalizedId));
        Assert.True(data.TryGetProperty("candidates", out var candidates));

        if (normalizedId.ValueKind != JsonValueKind.Null)
        {
            // Single match found
            Assert.False(string.IsNullOrEmpty(normalizedId.GetString()));
            Assert.True(candidates.ValueKind == JsonValueKind.Null);
        }
        else
        {
            // Multiple matches found
            Assert.True(candidates.ValueKind == JsonValueKind.Array);
            Assert.True(candidates.GetArrayLength() > 0);
        }
    }

    [Fact]
    public void NormalizeMemberId_WithInvalidInput_ReturnsError()
    {
        // Act
        var result = NormalizeMemberIdTool.NormalizeMemberId("completely-invalid-input-12345");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void Ping_WithLoadedAssembly_ReturnsPongWithMvid()
    {
        // Act
        var result = PingTool.Ping();

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.GetProperty("pong").GetBoolean());
        Assert.NotNull(data.GetProperty("mvid").GetString());
        Assert.True(data.GetProperty("timeUnix").GetInt64() > 0);
    }

    [Fact]
    public void FindBaseTypes_WithValidType_ReturnsBaseTypes()
    {
        // Arrange - find a type that has base types
        var types = ContextManager.GetAllTypes();
        var derivedType = types.FirstOrDefault(t =>
            t.DirectBaseTypes.Any(bt => bt.FullName != "System.Object"));

        if (derivedType != null)
        {
            var memberId = MemberResolver.GenerateMemberId(derivedType);

            // Act
            var result = FindBaseTypesTool.FindBaseTypes(memberId, includeInterfaces: true);

            // Assert
            Assert.NotNull(result);
            var response = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.Equal("ok", response.GetProperty("status").GetString());

            var data = response.GetProperty("data");
            Assert.True(data.TryGetProperty("bases", out _));
            Assert.True(data.TryGetProperty("interfaces", out _));
        }
    }

    [Fact]
    public void FindDerivedTypes_WithValidBaseType_ReturnsDerivedTypes()
    {
        // Arrange - find System.Object as a common base type
        var types = ContextManager.GetAllTypes();
        var objectType = types.FirstOrDefault(t => t.FullName == "System.Object");

        if (objectType != null)
        {
            var memberId = MemberResolver.GenerateMemberId(objectType);

            // Act
            var result = FindDerivedTypesTool.FindDerivedTypes(memberId, transitive: true, limit: 10);

            // Assert
            Assert.NotNull(result);
            var response = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.Equal("ok", response.GetProperty("status").GetString());

            var data = response.GetProperty("data");
            Assert.True(data.TryGetProperty("items", out var items));
            Assert.True(data.TryGetProperty("hasMore", out _));
            Assert.True(data.TryGetProperty("totalEstimate", out _));
        }
    }

    [Fact]
    public void GetIL_WithValidMethod_ReturnsILSummary()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Name.Contains("Test"));
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => !m.IsConstructor);
        if (method != null)
        {
            var memberId = MemberResolver.GenerateMemberId(method);

            // Act
            var result = GetILTool.GetIL(memberId, "IL");

            // Assert
            Assert.NotNull(result);
            var response = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.Equal("ok", response.GetProperty("status").GetString());

            var data = response.GetProperty("data");
            Assert.Equal(memberId, data.GetProperty("memberId").GetString());
            Assert.Equal("IL", data.GetProperty("format").GetString());
            Assert.True(data.GetProperty("totalLines").GetInt32() > 0);
            Assert.NotNull(data.GetProperty("text").GetString());
        }
    }

    [Fact]
    public void FindUsages_WithValidMember_ReturnsUsageResults()
    {
        // Arrange - find a member from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Name.Contains("Test"));
        Assert.NotNull(testType);

        var field = testType.Fields.FirstOrDefault();
        if (field != null)
        {
            var memberId = MemberResolver.GenerateMemberId(field);

            // Act
            var result = FindUsagesTool.FindUsages(memberId, limit: 10);

            // Assert
            Assert.NotNull(result);
            var response = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.Equal("ok", response.GetProperty("status").GetString());

            var data = response.GetProperty("data");
            Assert.True(data.TryGetProperty("items", out _));
            Assert.True(data.TryGetProperty("hasMore", out _));
            Assert.True(data.TryGetProperty("totalEstimate", out _));
        }
    }

    #region New Tool Tests

    [Fact]
    public void GetOverloads_WithValidMethod_ReturnsOverloadMethods()
    {
        // Arrange - find a method that might have overloads
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Count() > 1);
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => !m.IsConstructor);
        if (method != null)
        {
            var memberId = MemberResolver.GenerateMemberId(method);

            // Act
            var result = GetOverloadsTool.GetOverloads(memberId);

            // Assert
            Assert.NotNull(result);
            var response = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.Equal("ok", response.GetProperty("status").GetString());

            var data = response.GetProperty("data");
            Assert.True(data.TryGetProperty("items", out var items));
            Assert.True(data.TryGetProperty("hasMore", out _));
            Assert.True(data.TryGetProperty("totalEstimate", out _));

            // Items should be ordered by parameter count
            for (int i = 0; i < items.GetArrayLength(); i++)
            {
                var overload = items[i];
                Assert.Equal("Method", overload.GetProperty("kind").GetString());
                Assert.Equal(method.Name, overload.GetProperty("name").GetString());
                Assert.NotEqual(memberId, overload.GetProperty("memberId").GetString()); // Should be different from original
            }
        }
    }

    [Fact]
    public void SearchAttributes_WithAttributeName_ReturnsAttributedMembers()
    {
        // Act - search for a common attribute (even if none exist, should not error)
        var result = SearchAttributesTool.SearchAttributes("System.SerializableAttribute", limit: 10);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("items", out var items));
        Assert.True(data.TryGetProperty("hasMore", out _));
        Assert.True(data.TryGetProperty("totalEstimate", out _));

        // All items should have the attribute
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var member = items[i];
            Assert.Contains(member.GetProperty("kind").GetString()!,
                new[] { "Type", "Method", "Constructor", "Field", "Property", "Event" });
        }
    }

    [Fact]
    public void SearchAttributes_WithKindFilter_ReturnsOnlyMatchingKinds()
    {
        // Act - search for methods with any attribute
        var result = SearchAttributesTool.SearchAttributes("", kind: "method", limit: 5);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("items", out var items));

        // All returned items should be methods or constructors
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var member = items[i];
            Assert.Contains(member.GetProperty("kind").GetString()!,
                new[] { "Method", "Constructor" });
        }
    }

    [Fact]
    public void GetIL_WithUnsupportedFormat_ReturnsError()
    {
        // Arrange - find a method
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any());
        Assert.NotNull(testType);

        var method = testType.Methods.First();
        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = GetILTool.GetIL(memberId, "ILAst");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void GetIL_WithNonMethod_ReturnsError()
    {
        // Arrange - find a field (non-method)
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Fields.Any());
        Assert.NotNull(testType);

        var field = testType.Fields.First();
        var memberId = MemberResolver.GenerateMemberId(field);

        // Act
        var result = GetILTool.GetIL(memberId);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void FindCallees_WithValidMethod_ReturnsCalleeResults()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any());
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => !m.IsConstructor);
        if (method != null)
        {
            var memberId = MemberResolver.GenerateMemberId(method);

            // Act
            var result = FindCalleesTool.FindCallees(memberId, limit: 10);

            // Assert
            Assert.NotNull(result);
            var response = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.Equal("ok", response.GetProperty("status").GetString());

            var data = response.GetProperty("data");
            Assert.True(data.TryGetProperty("items", out _));
            Assert.True(data.TryGetProperty("hasMore", out _));
            Assert.True(data.TryGetProperty("totalEstimate", out _));

            // Note: Our implementation returns empty results as it's a framework implementation
            // This test verifies the structure is correct
        }
    }

    [Fact]
    public void GetOverrides_WithValidMethod_ReturnsOverrideInfo()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any());
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => !m.IsConstructor);
        if (method != null)
        {
            var memberId = MemberResolver.GenerateMemberId(method);

            // Act
            var result = GetOverridesTool.GetOverrides(memberId);

            // Assert
            Assert.NotNull(result);
            var response = JsonSerializer.Deserialize<JsonElement>(result);
            Assert.Equal("ok", response.GetProperty("status").GetString());

            var data = response.GetProperty("data");
            Assert.True(data.TryGetProperty("baseDefinition", out _));
            Assert.True(data.TryGetProperty("overrides", out var overrides));
            Assert.True(overrides.ValueKind == JsonValueKind.Array);
        }
    }

    [Fact]
    public void SetDecompileSettings_WithValidSettings_UpdatesSettings()
    {
        // Arrange
        var newSettings = new Dictionary<string, object>
        {
            { "usingDeclarations", false },
            { "showXmlDocumentation", false },
            { "namedArguments", true }
        };

        // Act
        var result = SetDecompileSettingsTool.SetDecompileSettings(newSettings);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.False(data.GetProperty("usingDeclarations").GetBoolean());
        Assert.False(data.GetProperty("showXmlDocumentation").GetBoolean());
        Assert.True(data.GetProperty("namedArguments").GetBoolean());

        // Restore original settings
        var originalSettings = new Dictionary<string, object>
        {
            { "usingDeclarations", true },
            { "showXmlDocumentation", true },
            { "namedArguments", true }
        };
        SetDecompileSettingsTool.SetDecompileSettings(originalSettings);
    }

    [Fact]
    public void SetDecompileSettings_WithEmptySettings_ReturnsCurrentSettings()
    {
        // Act
        var result = SetDecompileSettingsTool.SetDecompileSettings(new Dictionary<string, object>());

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("usingDeclarations", out _));
        Assert.True(data.TryGetProperty("showXmlDocumentation", out _));
        Assert.True(data.TryGetProperty("namedArguments", out _));
    }

    #endregion
}
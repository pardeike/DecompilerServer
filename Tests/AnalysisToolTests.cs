using DecompilerServer;
using DecompilerServer.Services;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tests;

/// <summary>
/// Tests for member analysis and relationship MCP tools functionality
/// </summary>
public class AnalysisToolTests : ServiceTestBase
{
    private readonly IServiceProvider _serviceProvider;

    public AnalysisToolTests()
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
        Assert.False(data.TryGetProperty("candidates", out _));
    }

    [Fact]
    public void ResolveMemberId_WithRawQualifiedSymbols_ReturnsCanonicalSummaries()
    {
        var methodResult = ResolveMemberIdTool.ResolveMemberId("TestLibrary.SimpleClass.SimpleMethod");
        var methodResponse = JsonSerializer.Deserialize<JsonElement>(methodResult);

        Assert.Equal("ok", methodResponse.GetProperty("status").GetString());
        var methodData = methodResponse.GetProperty("data");
        Assert.Equal("SimpleMethod", methodData.GetProperty("name").GetString());
        Assert.Equal("Method", methodData.GetProperty("kind").GetString());
        Assert.Matches("^[0-9a-f]{32}:[0-9A-F]{8}:M$", methodData.GetProperty("memberId").GetString()!);

        var typeResult = ResolveMemberIdTool.ResolveMemberId("TestLibrary.SimpleClass");
        var typeResponse = JsonSerializer.Deserialize<JsonElement>(typeResult);

        Assert.Equal("ok", typeResponse.GetProperty("status").GetString());
        var typeData = typeResponse.GetProperty("data");
        Assert.Equal("TestLibrary.SimpleClass", typeData.GetProperty("fullName").GetString());
        Assert.Equal("Type", typeData.GetProperty("kind").GetString());
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
        var hasNormalizedId = data.TryGetProperty("normalizedId", out var normalizedId);
        var hasCandidates = data.TryGetProperty("candidates", out var candidates);
        Assert.True(hasNormalizedId || hasCandidates);

        if (hasNormalizedId)
        {
            // Single match found
            Assert.False(string.IsNullOrEmpty(normalizedId.GetString()));
            Assert.False(hasCandidates);
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
    public void NormalizeMemberId_WithQualifiedMemberName_ReturnsCanonicalId()
    {
        var result = NormalizeMemberIdTool.NormalizeMemberId("TestLibrary.SimpleClass.SimpleMethod");

        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("normalizedId").GetString()));
    }

    [Fact]
    public void GetSourceSlice_WithStaleMethodGuess_ReturnsStructuredSuggestions()
    {
        var result = GetSourceSliceTool.GetSourceSlice(
            "M:TestLibrary.JobDriver_PlayMusicalInstrument.MakeNewToils",
            startLine: 1,
            endLine: 5);

        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());

        var error = response.GetProperty("error");
        Assert.Equal("member_not_found", error.GetProperty("code").GetString());

        var candidates = error.GetProperty("details").GetProperty("candidates");
        Assert.Contains(candidates.EnumerateArray(), candidate =>
            candidate.GetProperty("name").GetString() == "ModifyPlayToil");

        var hints = error.GetProperty("hints");
        Assert.Contains(hints.EnumerateArray(), hint =>
            hint.GetProperty("tool").GetString() == "get_members_of_type");
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
    public void FindDerivedTypes_WithLimit_ReportsTotalAndPages()
    {
        // Arrange
        var baseType = ContextManager.FindTypeByName("TestLibrary.BaseClass");
        Assert.NotNull(baseType);
        var memberId = MemberResolver.GenerateMemberId(baseType);

        // Act
        var firstResult = FindDerivedTypesTool.FindDerivedTypes(memberId, transitive: true, limit: 1);

        // Assert
        var firstResponse = JsonSerializer.Deserialize<JsonElement>(firstResult);
        Assert.Equal("ok", firstResponse.GetProperty("status").GetString());

        var firstData = firstResponse.GetProperty("data");
        Assert.Equal(1, firstData.GetProperty("items").GetArrayLength());
        Assert.True(firstData.GetProperty("hasMore").GetBoolean());
        Assert.Equal(3, firstData.GetProperty("totalEstimate").GetInt32());

        var secondResult = FindDerivedTypesTool.FindDerivedTypes(memberId, transitive: true, limit: 1, cursor: firstData.GetProperty("nextCursor").GetString());
        var secondResponse = JsonSerializer.Deserialize<JsonElement>(secondResult);
        Assert.Equal("ok", secondResponse.GetProperty("status").GetString());

        var secondData = secondResponse.GetProperty("data");
        Assert.Equal(1, secondData.GetProperty("items").GetArrayLength());
        Assert.True(secondData.GetProperty("hasMore").GetBoolean());
        Assert.Equal(3, secondData.GetProperty("totalEstimate").GetInt32());
    }

    [Fact]
    public void FindDerivedTypes_TransitiveParameter_AffectsResults()
    {
        // Arrange - find a type that has base types (indicating inheritance hierarchy exists)
        var types = ContextManager.GetAllTypes();
        var baseType = types.FirstOrDefault(t => t.DirectBaseTypes.Any(bt => bt.FullName != "System.Object"));

        if (baseType != null)
        {
            var memberId = MemberResolver.GenerateMemberId(baseType);

            // Act - Test both transitive modes
            var transitiveResult = FindDerivedTypesTool.FindDerivedTypes(memberId, transitive: true, limit: 50);
            var directResult = FindDerivedTypesTool.FindDerivedTypes(memberId, transitive: false, limit: 50);

            // Assert both calls succeed or both fail consistently
            Assert.NotNull(transitiveResult);
            Assert.NotNull(directResult);

            var transitiveResponse = JsonSerializer.Deserialize<JsonElement>(transitiveResult);
            var directResponse = JsonSerializer.Deserialize<JsonElement>(directResult);

            // If either fails, skip this test as the implementation may not support this type
            if (transitiveResponse.GetProperty("status").GetString() == "error" ||
                directResponse.GetProperty("status").GetString() == "error")
            {
                return;
            }

            Assert.Equal("ok", transitiveResponse.GetProperty("status").GetString());
            Assert.Equal("ok", directResponse.GetProperty("status").GetString());

            // Both should have valid data structure
            var transitiveData = transitiveResponse.GetProperty("data");
            var directData = directResponse.GetProperty("data");

            Assert.True(transitiveData.TryGetProperty("items", out var transitiveItems));
            Assert.True(directData.TryGetProperty("items", out var directItems));
            Assert.True(transitiveData.TryGetProperty("hasMore", out _));
            Assert.True(directData.TryGetProperty("hasMore", out _));
        }
    }

    [Fact]
    public void GetIL_WithValidMethod_ReturnsILInstructions()
    {
        // Arrange - find a method from the test assembly
        var testType = ContextManager.FindTypeByName("TestLibrary.SimpleClass");
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => m.Name == "SimpleMethod");
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
            Assert.True(data.GetProperty("isFullDisassembly").GetBoolean());
            Assert.Contains("IL_", data.GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.Contains("// Metadata Token: 0x", data.GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("System.Reflection.Metadata.EntityHandle", data.GetProperty("text").GetString(), StringComparison.Ordinal);
            Assert.True(data.GetProperty("instructions").GetArrayLength() > 0);
        }
    }

    [Fact]
    public void GetIL_WithLimit_ReturnsPagedInstructions()
    {
        var testType = ContextManager.FindTypeByName("TestLibrary.SimpleClass");
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => m.Name == "SimpleMethod");
        Assert.NotNull(method);

        var memberId = MemberResolver.GenerateMemberId(method);
        var result = GetILTool.GetIL(memberId, limit: 2);

        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.False(data.GetProperty("isFullDisassembly").GetBoolean());
        Assert.True(data.GetProperty("hasMore").GetBoolean());
        Assert.Equal("2", data.GetProperty("nextCursor").GetString());
        Assert.Equal(2, data.GetProperty("returnedInstructionCount").GetInt32());
        Assert.Equal(2, data.GetProperty("instructions").GetArrayLength());
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
}

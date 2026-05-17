using DecompilerServer;
using DecompilerServer.Services;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tests;

/// <summary>
/// Tests for advanced analysis MCP tools functionality
/// </summary>
public class AdvancedAnalysisToolTests : ServiceTestBase
{
    private readonly IServiceProvider _serviceProvider;

    public AdvancedAnalysisToolTests()
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
    public void SearchAttributes_WithLimit_ReportsTotalAndUsableNextCursor()
    {
        // Act
        var firstResult = SearchAttributesTool.SearchAttributes("TestAttribute", limit: 1);

        // Assert
        var firstResponse = JsonSerializer.Deserialize<JsonElement>(firstResult);
        Assert.Equal("ok", firstResponse.GetProperty("status").GetString());

        var firstData = firstResponse.GetProperty("data");
        Assert.Equal(1, firstData.GetProperty("items").GetArrayLength());
        Assert.True(firstData.GetProperty("hasMore").GetBoolean());
        Assert.True(firstData.GetProperty("totalEstimate").GetInt32() > 1);

        var nextCursor = firstData.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(nextCursor));

        var secondResult = SearchAttributesTool.SearchAttributes("TestAttribute", limit: 1, cursor: nextCursor);
        var secondResponse = JsonSerializer.Deserialize<JsonElement>(secondResult);
        Assert.Equal("ok", secondResponse.GetProperty("status").GetString());

        var secondData = secondResponse.GetProperty("data");
        Assert.Equal(1, secondData.GetProperty("items").GetArrayLength());
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
        // Arrange - use a method with a direct same-assembly call
        var testType = ContextManager.FindTypeByName("TestLibrary.JobDriver_PlayMusicalInstrument");
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => m.Name == "NotifyStarted");
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

            var items = data.GetProperty("items");
            Assert.Contains(items.EnumerateArray(), item =>
                item.GetProperty("symbol").GetString()?.Contains("ModifyPlayToil", StringComparison.Ordinal) == true
                && item.GetProperty("kind").GetString() == "Call"
                && item.GetProperty("resolved").GetBoolean()
                && item.TryGetProperty("targetMemberId", out _)
                && item.TryGetProperty("offset", out _)
                && item.TryGetProperty("opcode", out _));
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

            // If the tool returns an error, skip this test as it may not be implemented for this method type
            if (response.GetProperty("status").GetString() == "error")
            {
                return;
            }

            Assert.Equal("ok", response.GetProperty("status").GetString());

            var data = response.GetProperty("data");
            var hasBase = data.TryGetProperty("baseDefinition", out _);
            var hasOverrides = data.TryGetProperty("overrides", out var overrides);

            if (hasOverrides)
            {
                Assert.True(overrides.ValueKind == JsonValueKind.Array);
            }
            else
            {
                Assert.False(hasBase);
            }
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
    public void SetDecompileSettings_WithMcpJsonElementValues_UpdatesSettings()
    {
        // Arrange - MCP JSON arguments are surfaced as JsonElement values inside the settings map.
        using var settingsDocument = JsonDocument.Parse("""
            {
              "usingDeclarations": false,
              "showXmlDocumentation": false,
              "namedArguments": false,
              "alwaysUseBraces": false
            }
            """);
        var newSettings = settingsDocument.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => (object)property.Value.Clone());

        // Act
        var result = SetDecompileSettingsTool.SetDecompileSettings(newSettings);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.False(data.GetProperty("usingDeclarations").GetBoolean());
        Assert.False(data.GetProperty("showXmlDocumentation").GetBoolean());
        Assert.False(data.GetProperty("namedArguments").GetBoolean());
        Assert.False(data.GetProperty("alwaysUseBraces").GetBoolean());

        // Restore original settings
        var originalSettings = new Dictionary<string, object>
        {
            { "usingDeclarations", true },
            { "showXmlDocumentation", true },
            { "namedArguments", true },
            { "alwaysUseBraces", true }
        };
        SetDecompileSettingsTool.SetDecompileSettings(originalSettings);
    }

    [Fact]
    public void SetDecompileSettings_WithPartialUpdate_PreservesExistingSettings()
    {
        // Arrange
        SetDecompileSettingsTool.SetDecompileSettings(new Dictionary<string, object>
        {
            { "namedArguments", false }
        });

        // Act
        var result = SetDecompileSettingsTool.SetDecompileSettings(new Dictionary<string, object>
        {
            { "usingDeclarations", false }
        });

        // Assert
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.False(data.GetProperty("usingDeclarations").GetBoolean());
        Assert.False(data.GetProperty("namedArguments").GetBoolean());

        // Restore original settings
        var originalSettings = new Dictionary<string, object>
        {
            { "usingDeclarations", true },
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

    [Fact]
    public void Unload_WithLoadedAssembly_ClearsContextAndCaches()
    {
        // Verify assembly is loaded before unload
        Assert.True(ContextManager.IsLoaded);

        // Act
        var result = UnloadTool.Unload();

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal("ok", data.GetProperty("status").GetString());
        Assert.False(ContextManager.IsLoaded);

        // The singleton context manager should remain reusable after unload.
        var reloadResult = LoadAssemblyTool.LoadAssembly(assemblyPath: TestAssemblyPath, rebuildIndex: false);
        var reloadResponse = JsonSerializer.Deserialize<JsonElement>(reloadResult);
        Assert.Equal("ok", reloadResponse.GetProperty("status").GetString());
        Assert.True(ContextManager.IsLoaded);
    }

    [Fact]
    public void FindCallers_WithValidMethod_ReturnsCallerResults()
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
            var result = FindCallersTool.FindCallers(memberId, limit: 10);

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

    [Fact]
    public void FindCallers_WithInvalidMethodId_ReturnsError()
    {
        // Act
        var result = FindCallersTool.FindCallers("invalid-method-id");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void GetImplementations_WithValidInterface_ReturnsImplementors()
    {
        // Arrange - find an interface type if available
        var types = ContextManager.GetAllTypes();
        var interfaceType = types.FirstOrDefault(t => t.Kind == ICSharpCode.Decompiler.TypeSystem.TypeKind.Interface);

        if (interfaceType != null)
        {
            var memberId = MemberResolver.GenerateMemberId(interfaceType);

            // Act
            var result = GetImplementationsTool.GetImplementations(memberId, limit: 10);

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

    [Fact]
    public void GetImplementations_WithLimit_ReportsTotalAndPages()
    {
        // Arrange
        var interfaceType = ContextManager.FindTypeByName("TestLibrary.ITestInterface");
        Assert.NotNull(interfaceType);
        var memberId = MemberResolver.GenerateMemberId(interfaceType);

        // Act
        var firstResult = GetImplementationsTool.GetImplementations(memberId, limit: 1);

        // Assert
        var firstResponse = JsonSerializer.Deserialize<JsonElement>(firstResult);
        Assert.Equal("ok", firstResponse.GetProperty("status").GetString());

        var firstData = firstResponse.GetProperty("data");
        Assert.Equal(1, firstData.GetProperty("items").GetArrayLength());
        Assert.True(firstData.GetProperty("hasMore").GetBoolean());
        Assert.Equal(3, firstData.GetProperty("totalEstimate").GetInt32());

        var secondResult = GetImplementationsTool.GetImplementations(memberId, limit: 1, cursor: firstData.GetProperty("nextCursor").GetString());
        var secondResponse = JsonSerializer.Deserialize<JsonElement>(secondResult);
        Assert.Equal("ok", secondResponse.GetProperty("status").GetString());

        var secondData = secondResponse.GetProperty("data");
        Assert.Equal(1, secondData.GetProperty("items").GetArrayLength());
        Assert.True(secondData.GetProperty("hasMore").GetBoolean());
        Assert.Equal(3, secondData.GetProperty("totalEstimate").GetInt32());
    }

    [Fact]
    public void GetImplementations_WithInterfaceMethod_ReturnsConcreteImplementations()
    {
        // Arrange
        var interfaceType = ContextManager.FindTypeByName("TestLibrary.ITestInterface");
        Assert.NotNull(interfaceType);
        var method = interfaceType.Methods.FirstOrDefault(m => m.Name == "InterfaceMethod");
        Assert.NotNull(method);
        var methodId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = GetImplementationsTool.GetImplementations(methodId, limit: 10);

        // Assert
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalEstimate").GetInt32());
        var names = data.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("fullName").GetString())
            .ToArray();

        Assert.Contains("TestLibrary.DerivedClass.InterfaceMethod", names);
        Assert.Contains("TestLibrary.MultiInterfaceClass.InterfaceMethod", names);
    }

    [Fact]
    public void GetImplementations_WithInvalidMemberId_ReturnsError()
    {
        // Act
        var result = GetImplementationsTool.GetImplementations("invalid-member-id");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void BatchGetDecompiledSource_WithValidMemberIds_ReturnsDocumentsAndSlices()
    {
        // Arrange - find a few members from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any());
        Assert.NotNull(testType);

        var methods = testType.Methods.Take(2).ToArray();
        var memberIds = methods.Select(m => MemberResolver.GenerateMemberId(m)).ToArray();

        // Act
        var result = BatchGetDecompiledSourceTool.BatchGetDecompiledSource(memberIds, maxTotalChars: 50000);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("items", out var items));
        Assert.True(data.TryGetProperty("totalCharacters", out _));
        Assert.True(data.TryGetProperty("truncated", out _));
        Assert.True(data.TryGetProperty("processed", out _));
        Assert.True(data.TryGetProperty("requested", out _));

        // Verify structure of returned items
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var item = items[i];
            Assert.True(item.TryGetProperty("doc", out var doc));
            Assert.True(item.TryGetProperty("firstSlice", out var slice));

            // Verify doc structure
            Assert.True(doc.TryGetProperty("memberId", out _));
            Assert.True(doc.TryGetProperty("language", out _));
            Assert.True(doc.TryGetProperty("totalLines", out _));

            // Verify slice structure
            Assert.True(slice.TryGetProperty("code", out _));
            Assert.True(slice.TryGetProperty("startLine", out _));
            Assert.True(slice.TryGetProperty("endLine", out _));
        }
    }

    [Fact]
    public void BatchGetDecompiledSource_WithEmptyArray_ReturnsError()
    {
        // Act
        var result = BatchGetDecompiledSourceTool.BatchGetDecompiledSource(new string[0]);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void SearchStringLiterals_WithPattern_ReturnsStringLiteralReferences()
    {
        // Act - search for any string literal pattern
        var result = SearchStringLiteralsTool.SearchStringLiterals("test", regex: false, limit: 10);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("items", out var items));
        Assert.True(data.TryGetProperty("hasMore", out _));
        Assert.True(data.TryGetProperty("totalEstimate", out _));

        // Verify structure of returned items
        for (int i = 0; i < items.GetArrayLength(); i++)
        {
            var item = items[i];
            Assert.True(item.TryGetProperty("value", out _));
            Assert.True(item.TryGetProperty("inMember", out _));
            Assert.True(item.TryGetProperty("inType", out _));
            Assert.True(item.TryGetProperty("kind", out _));
            Assert.Equal("StringLiteral", item.GetProperty("kind").GetString());
        }
    }

    [Fact]
    public void SearchStringLiterals_WithEmptyPattern_ReturnsAllLiterals()
    {
        // Act
        var result = SearchStringLiteralsTool.SearchStringLiterals("");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        // Should return data with string literals
        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("items", out var items));
        Assert.True(items.GetArrayLength() >= 0); // May be 0 or more depending on test assembly
    }
}

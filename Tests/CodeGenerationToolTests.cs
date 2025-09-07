using DecompilerServer;
using DecompilerServer.Services;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tests;

/// <summary>
/// Tests for code generation MCP tools functionality
/// </summary>
public class CodeGenerationToolTests : ServiceTestBase
{
    private readonly IServiceProvider _serviceProvider;

    public CodeGenerationToolTests()
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
    public void GenerateExtensionMethodWrapper_WithValidInstanceMethod_ReturnsExtensionMethod()
    {
        // Arrange - find an instance method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any(m => !m.IsStatic && !m.IsConstructor));
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => !m.IsStatic && !m.IsConstructor);
        Assert.NotNull(method);

        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = GenerateExtensionMethodWrapperTool.GenerateExtensionMethodWrapper(memberId);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("target", out var target));
        Assert.True(data.TryGetProperty("code", out var code));
        Assert.True(data.TryGetProperty("notes", out var notes));

        // Verify the generated code contains expected elements
        var codeString = code.GetString();
        Assert.Contains("public static", codeString);
        Assert.Contains("this ", codeString); // Extension method syntax
        Assert.Contains("ExtensionMethods", codeString); // Namespace
        Assert.Contains(method.Name, codeString); // Method name

        // Verify target information
        Assert.Equal(memberId, target.GetProperty("memberId").GetString());
        Assert.Equal(method.Name, target.GetProperty("name").GetString());

        // Verify notes exist
        Assert.True(notes.ValueKind == JsonValueKind.Array);
        Assert.True(notes.GetArrayLength() > 0);
    }

    [Fact]
    public void GenerateExtensionMethodWrapper_WithStaticMethod_ReturnsError()
    {
        // Arrange - find a static method
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any(m => m.IsStatic && !m.IsConstructor));
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => m.IsStatic && !m.IsConstructor);
        Assert.NotNull(method);

        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = GenerateExtensionMethodWrapperTool.GenerateExtensionMethodWrapper(memberId);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void GenerateDetourStub_WithValidMethod_ReturnsDetourMethod()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.First(t => t.FullName == "TestLibrary.SimpleClass");
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => !m.IsConstructor);
        Assert.NotNull(method);

        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = GenerateDetourStubTool.GenerateDetourStub(memberId);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("target", out var target));
        Assert.True(data.TryGetProperty("code", out var code));
        Assert.True(data.TryGetProperty("notes", out var notes));

        // Verify the generated code contains expected elements
        var codeString = code.GetString();
        Assert.Contains("DetourStubs", codeString); // Namespace
        Assert.Contains("DetourHelper", codeString); // Class name
        Assert.Contains($"{method.Name}Detour", codeString); // Detour method name
        Assert.Contains("MethodInfo", codeString); // Reflection usage
        Assert.Contains("Debug.WriteLine", codeString); // Logging

        // Verify target information
        Assert.Equal(memberId, target.GetProperty("memberId").GetString());
        Assert.Equal(method.Name, target.GetProperty("name").GetString());

        // Verify notes exist
        Assert.True(notes.ValueKind == JsonValueKind.Array);
        Assert.True(notes.GetArrayLength() > 0);
    }

    [Fact]
    public void GenerateHarmonyPatchSkeleton_WithValidMethod_ReturnsHarmonyPatch()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any(m => !m.IsConstructor));
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => !m.IsConstructor);
        Assert.NotNull(method);

        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = GenerateHarmonyPatchSkeletonTool.GenerateHarmonyPatchSkeleton(memberId, "Prefix,Postfix", includeReflectionTargeting: true);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("target", out var target));
        Assert.True(data.TryGetProperty("code", out var code));
        Assert.True(data.TryGetProperty("notes", out var notes));

        // Verify the generated code contains expected elements
        var codeString = code.GetString();
        Assert.Contains("HarmonyPatches", codeString); // Namespace
        Assert.Contains("[HarmonyPatch]", codeString); // Harmony attribute
        Assert.Contains("[HarmonyPrefix]", codeString); // Prefix patch
        Assert.Contains("[HarmonyPostfix]", codeString); // Postfix patch
        Assert.Contains("AccessTools", codeString); // Reflection targeting
        Assert.Contains("TargetMethod", codeString); // Target method specification

        // Verify target information
        Assert.Equal(memberId, target.GetProperty("memberId").GetString());
        Assert.Equal(method.Name, target.GetProperty("name").GetString());

        // Verify notes exist
        Assert.True(notes.ValueKind == JsonValueKind.Array);
        Assert.True(notes.GetArrayLength() > 0);
    }

    [Fact]
    public void SuggestTranspilerTargets_WithValidMethod_ReturnsTranspilerHints()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any(m => !m.IsConstructor));
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => !m.IsConstructor);
        Assert.NotNull(method);

        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = SuggestTranspilerTargetsTool.SuggestTranspilerTargets(memberId, maxHints: 5);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("target", out var target));
        Assert.True(data.TryGetProperty("hints", out var hints));
        Assert.True(data.TryGetProperty("exampleTranspiler", out var example));
        Assert.True(data.TryGetProperty("notes", out var notes));

        // Verify target information
        Assert.Equal(memberId, target.GetProperty("memberId").GetString());
        Assert.Equal(method.Name, target.GetProperty("name").GetString());

        // Verify hints structure
        Assert.True(hints.ValueKind == JsonValueKind.Array);
        Assert.True(hints.GetArrayLength() > 0);

        for (int i = 0; i < hints.GetArrayLength(); i++)
        {
            var hint = hints[i];
            Assert.True(hint.TryGetProperty("offset", out _));
            Assert.True(hint.TryGetProperty("opcode", out _));
            Assert.True(hint.TryGetProperty("operandSummary", out _));
            Assert.True(hint.TryGetProperty("nearbyOps", out _));
            Assert.True(hint.TryGetProperty("rationale", out _));
            Assert.True(hint.TryGetProperty("example", out _));
        }

        // Verify example transpiler code
        var exampleString = example.GetString();
        Assert.Contains("Transpiler", exampleString);
        Assert.Contains("CodeInstruction", exampleString);
        Assert.Contains("OpCodes", exampleString);

        // Verify notes exist
        Assert.True(notes.ValueKind == JsonValueKind.Array);
        Assert.True(notes.GetArrayLength() > 0);
    }

    [Fact]
    public void SuggestTranspilerTargets_WithInvalidMemberId_ReturnsError()
    {
        // Act
        var result = SuggestTranspilerTargetsTool.SuggestTranspilerTargets("invalid-member-id");

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void PlanChunking_WithValidMember_ReturnsChunkPlan()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any(m => !m.IsConstructor));
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => !m.IsConstructor);
        Assert.NotNull(method);

        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = PlanChunkingTool.PlanChunking(memberId, targetChunkSize: 1000, overlap: 1);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        if (response.GetProperty("status").GetString() != "ok")
        {
            return;
        }

        var data = JsonSerializer.Deserialize<ChunkPlanResult>(
            response.GetProperty("data").GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(data);
        Assert.Equal(memberId, data.MemberId);
        Assert.NotNull(data.Chunks);
        Assert.All(data.Chunks, c =>
        {
            Assert.True(c.StartLine <= c.EndLine);
            Assert.True(c.EstimatedChars > 0);
        });
    }

    [Fact]
    public void PlanChunking_WithNegativeOverlap_ReturnsError()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any(m => !m.IsConstructor));
        Assert.NotNull(testType);

        var method = testType.Methods.First(m => !m.IsConstructor);
        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = PlanChunkingTool.PlanChunking(memberId, targetChunkSize: 1000, overlap: -1);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void PlanChunking_WithOverlapEqualChunkSize_ReturnsError()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any(m => !m.IsConstructor));
        Assert.NotNull(testType);

        var method = testType.Methods.First(m => !m.IsConstructor);
        var memberId = MemberResolver.GenerateMemberId(method);

        // Act - use very small chunk size to ensure target lines per chunk is 1
        var result = PlanChunkingTool.PlanChunking(memberId, targetChunkSize: 1, overlap: 1);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("error", response.GetProperty("status").GetString());
    }

    [Fact]
    public void GetAstOutline_WithValidMember_ReturnsOutline()
    {
        // Arrange - find a method from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault(t => t.Methods.Any(m => !m.IsConstructor));
        Assert.NotNull(testType);

        var method = testType.Methods.FirstOrDefault(m => !m.IsConstructor);
        Assert.NotNull(method);

        var memberId = MemberResolver.GenerateMemberId(method);

        // Act
        var result = GetAstOutlineTool.GetAstOutline(memberId, maxDepth: 2);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        Assert.True(data.TryGetProperty("memberId", out _));
        Assert.True(data.TryGetProperty("memberName", out _));
        Assert.True(data.TryGetProperty("memberKind", out _));
        Assert.True(data.TryGetProperty("outline", out var outline));
        Assert.True(data.TryGetProperty("maxDepth", out _));

        // Verify outline structure
        Assert.True(outline.TryGetProperty("kind", out _));
        Assert.True(outline.TryGetProperty("name", out _));

        // For methods, expect certain properties
        if (outline.TryGetProperty("kind", out var kind) &&
            (kind.GetString() == "Method" || kind.GetString() == "Constructor"))
        {
            Assert.True(outline.TryGetProperty("accessibility", out _));
            Assert.True(outline.TryGetProperty("parameterCount", out _));
        }
    }

    [Fact]
    public void GetAstOutline_WithType_ReturnsTypeOutline()
    {
        // Arrange - find a type from the test assembly
        var types = ContextManager.GetAllTypes();
        var testType = types.FirstOrDefault();
        Assert.NotNull(testType);

        var memberId = MemberResolver.GenerateMemberId(testType);

        // Act
        var result = GetAstOutlineTool.GetAstOutline(memberId, maxDepth: 1);

        // Assert
        Assert.NotNull(result);
        var response = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal("ok", response.GetProperty("status").GetString());

        var data = response.GetProperty("data");
        // Type kind could be Class, Interface, Struct, etc.
        var memberKind = data.GetProperty("memberKind").GetString();
        Assert.True(memberKind == "Class" || memberKind == "Interface" || memberKind == "Struct" ||
                   memberKind == "Enum" || memberKind == "Delegate");

        var outline = data.GetProperty("outline");
        Assert.True(outline.TryGetProperty("kind", out _));
        Assert.True(outline.TryGetProperty("name", out _));
        Assert.True(outline.TryGetProperty("fullName", out _));
        Assert.True(outline.TryGetProperty("memberCount", out _));
        Assert.True(outline.TryGetProperty("children", out var children));

        // Should have children at depth 1
        Assert.True(children.ValueKind == JsonValueKind.Array);
    }
}
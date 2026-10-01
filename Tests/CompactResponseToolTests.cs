using DecompilerServer;
using DecompilerServer.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Tests;

[Collection(ServiceLocatorIsolationCollection.Name)]
public sealed class CompactResponseToolTests : ServiceTestBase, IDisposable
{
    private readonly ServiceProvider _provider;

    public CompactResponseToolTests()
    {
        _provider = CreateProvider(extended: false);
        ServiceLocator.SetServiceProvider(_provider);
    }

    private ServiceProvider CreateProvider(bool extended)
    {
        var services = new ServiceCollection();
        services.AddSingleton(ContextManager);
        services.AddSingleton(MemberResolver);
        services.AddSingleton<DecompilerService>();
        services.AddSingleton<UsageAnalyzer>();
        services.AddSingleton<InheritanceAnalyzer>();
        services.AddSingleton(new ResponseFormatOptions(extended));
        return services.BuildServiceProvider();
    }

    private string Extended(Func<string> call)
    {
        using var provider = CreateProvider(extended: true);
        ServiceLocator.SetServiceProvider(provider);
        try
        {
            return call();
        }
        finally
        {
            ServiceLocator.SetServiceProvider(_provider);
        }
    }

    private static JsonElement Data(string json) => JsonSerializer.Deserialize<JsonElement>(json).GetProperty("data");

    [Theory]
    [InlineData("M:TestLibrary.SimpleClass.SimpleMethod", false)]
    [InlineData("T:TestLibrary.SimpleClass", true)]
    [InlineData("T:TestLibrary.SimpleClass", false)]
    public void Source_PreservesEveryLineAndProvenance(string symbol, bool includeHeader)
    {
        var compactJson = GetDecompiledSourceTool.GetDecompiledSource(symbol, includeHeader);
        var extendedJson = Extended(() => GetDecompiledSourceTool.GetDecompiledSource(symbol, includeHeader));
        var compact = Data(compactJson);
        var extended = Data(extendedJson);

        var originalLines = extended.GetProperty("lines").EnumerateArray().Select(line => line.GetString()!).ToArray();
        Assert.Equal(string.Join('\n', originalLines), compact.GetProperty("code").GetString());
        Assert.Equal(extended.GetProperty("totalLines").GetInt32(), compact.GetProperty("code").GetString()!.Split('\n').Length);
        Assert.Equal(extended.GetProperty("sourceKind").GetString(), compact.GetProperty("sourceKind").GetString());
        if (extended.TryGetProperty("sourcePath", out var path))
            Assert.Equal(path.GetString(), compact.GetProperty("sourcePath").GetString());
        Assert.False(compact.TryGetProperty("language", out _));
        Assert.False(compact.TryGetProperty("hash", out _));
        Assert.False(compact.TryGetProperty("lines", out _));
        Assert.False(compact.TryGetProperty("includeHeader", out _));
        Assert.NotEmpty(extended.GetProperty("hash").GetString()!);
        Assert.True(compactJson.Length < extendedJson.Length);
    }

    [Fact]
    public void Source_PreservesNonCSharpLanguageAndTrailingBlankLines()
    {
        var document = new SourceDocument("id", "VB", 3, "hash", ["Example", "", ""], true,
            SourceKinds.LocalSource, "Example.vb", "https://example.test/Example.vb");
        var compact = Data(ResponseFormatter.Success(document));

        Assert.Equal("VB", compact.GetProperty("language").GetString());
        Assert.Equal("Example\n\n", compact.GetProperty("code").GetString());
        Assert.Equal("Example.vb", compact.GetProperty("sourcePath").GetString());
        Assert.Equal(document.SourceUri, compact.GetProperty("sourceUri").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Slice_PreservesClampedBoundsAndNumberedContent(bool includeLineNumbers)
    {
        const string symbol = "M:TestLibrary.SimpleClass.SimpleMethod";
        var compact = Data(GetSourceSliceTool.GetSourceSlice(symbol, 1, 10000, includeLineNumbers, context: 2));
        var extended = Data(Extended(() => GetSourceSliceTool.GetSourceSlice(symbol, 1, 10000, includeLineNumbers, context: 2)));

        foreach (var field in new[] { "startLine", "endLine", "totalLines", "code", "sourceKind" })
            Assert.Equal(extended.GetProperty(field).ToString(), compact.GetProperty(field).ToString());
        Assert.Equal(1, compact.GetProperty("startLine").GetInt32());
        Assert.Equal(compact.GetProperty("totalLines").GetInt32(), compact.GetProperty("endLine").GetInt32());
        Assert.False(compact.TryGetProperty("hash", out _));
        Assert.True(extended.TryGetProperty("hash", out _));
    }

    [Fact]
    public void Error_PreservesRecoveryCandidatesAndHintsWithoutDuplicatingDetails()
    {
        const string missing = "TestLibrary.SimpleClass.MissingMember";
        var compactJson = ResolveMemberIdTool.ResolveMemberId(missing);
        var compact = JsonSerializer.Deserialize<JsonElement>(compactJson);
        var extendedJson = Extended(() => ResolveMemberIdTool.ResolveMemberId(missing));
        var extended = JsonSerializer.Deserialize<JsonElement>(extendedJson);
        var error = compact.GetProperty("error");
        var originalError = extended.GetProperty("error");

        Assert.Equal("error", compact.GetProperty("status").GetString());
        Assert.False(compact.TryGetProperty("details", out _));
        Assert.False(compact.TryGetProperty("message", out _));
        Assert.Equal("member_not_found", error.GetProperty("code").GetString());
        Assert.Equal(originalError.GetProperty("message").GetString(), error.GetProperty("message").GetString());
        Assert.True(JsonElement.DeepEquals(originalError.GetProperty("hints"), error.GetProperty("hints")));
        var candidates = error.GetProperty("details").GetProperty("candidates").EnumerateArray().ToArray();
        var originalCandidates = originalError.GetProperty("details").GetProperty("candidates").EnumerateArray().ToArray();
        Assert.NotEmpty(candidates);
        Assert.Equal(originalCandidates.Length, candidates.Length);
        for (var index = 0; index < candidates.Length; index++)
            foreach (var field in new[] { "memberId", "name", "kind", "signature", "accessibility", "isStatic", "isAbstract", "isVirtual" })
                Assert.Equal(originalCandidates[index].GetProperty(field).ToString(), candidates[index].GetProperty(field).ToString());
        Assert.True(extended.TryGetProperty("details", out _));
        Assert.True(compactJson.Length < extendedJson.Length);
    }

    [Fact]
    public void Callees_PreserveTargetsAndCallSitesWithoutCompatibilityAliases()
    {
        const string symbol = "M:TestLibrary.SimpleClass.SimpleMethod";
        var compact = Data(FindCalleesTool.FindCallees(symbol)).GetProperty("items").EnumerateArray().ToArray();
        var extended = Data(Extended(() => FindCalleesTool.FindCallees(symbol))).GetProperty("items").EnumerateArray().ToArray();

        Assert.NotEmpty(compact);
        Assert.Equal(extended.Length, compact.Length);
        for (var index = 0; index < compact.Length; index++)
        {
            foreach (var field in new[] { "symbol", "kind", "opcode", "offset", "resolution", "operandTokenHex" })
                Assert.Equal(extended[index].GetProperty(field).ToString(), compact[index].GetProperty(field).ToString());
            if (extended[index].TryGetProperty("targetMemberId", out var target))
                Assert.Equal(target.GetString(), compact[index].GetProperty("targetMemberId").GetString());
            foreach (var removed in new[] { "inMember", "inType", "resolved", "operandToken" })
                Assert.False(compact[index].TryGetProperty(removed, out _));
        }
    }

    [Fact]
    public void IL_PreservesInstructionOperandsAndBodyMetadata()
    {
        const string symbol = "M:TestLibrary.SimpleClass.MethodWithParameters";
        var compact = Data(GetILTool.GetIL(symbol));
        var extended = Data(Extended(() => GetILTool.GetIL(symbol)));
        var instructions = compact.GetProperty("instructions").EnumerateArray().ToArray();
        var originalInstructions = extended.GetProperty("instructions").EnumerateArray().ToArray();

        Assert.NotEmpty(instructions);
        Assert.Equal(originalInstructions.Length, instructions.Length);
        Assert.Equal(extended.GetProperty("totalInstructionCount").GetInt32(), compact.GetProperty("totalInstructionCount").GetInt32());
        for (var index = 0; index < instructions.Length; index++)
            foreach (var field in originalInstructions[index].EnumerateObject())
            {
                if (field.Name == "operandToken" || field.Name == "operand" && originalInstructions[index].TryGetProperty("operandToken", out _))
                    continue;
                Assert.True(JsonElement.DeepEquals(field.Value, instructions[index].GetProperty(field.Name)));
            }
        foreach (var field in compact.GetProperty("body").EnumerateObject())
            Assert.True(JsonElement.DeepEquals(field.Value, extended.GetProperty("body").GetProperty(field.Name)));
        Assert.False(compact.TryGetProperty("text", out _));
        Assert.Contains("IL_", extended.GetProperty("text").GetString()!);
    }

    [Fact]
    public void IL_WithoutBodyRetainsMethodSummary()
    {
        var data = Data(GetILTool.GetIL("M:TestLibrary.BaseClass.AbstractMethod"));

        Assert.Equal("no_il_body", data.GetProperty("noBodyReason").GetString());
        Assert.Empty(data.GetProperty("instructions").EnumerateArray());
        var method = data.GetProperty("method");
        Assert.Equal("TestLibrary.BaseClass.AbstractMethod", method.GetProperty("fullName").GetString());
        Assert.Equal("System.Void", method.GetProperty("returnType").GetString());
        Assert.True(method.GetProperty("isAbstract").GetBoolean());
        Assert.Matches("^[0-9a-f]{32}:[0-9A-F]{8}:M$", method.GetProperty("memberId").GetString()!);
    }

    [Theory]
    [InlineData("StaticMethod")]
    [InlineData("VirtualMethod")]
    public void Signature_PreservesIdentityAndModifiers(string name)
    {
        var symbol = "M:TestLibrary.SimpleClass." + name;
        var compact = Data(GetMemberSignatureTool.GetMemberSignature(symbol));
        var extended = Data(Extended(() => GetMemberSignatureTool.GetMemberSignature(symbol)));

        foreach (var field in compact.EnumerateObject())
            Assert.True(JsonElement.DeepEquals(field.Value, extended.GetProperty("summary").GetProperty(field.Name)));
        Assert.Equal(extended.GetProperty("signature").GetString(), compact.GetProperty("signature").GetString());
        Assert.False(compact.TryGetProperty("summary", out _));
        Assert.True(compact.GetProperty(name == "StaticMethod" ? "isStatic" : "isVirtual").GetBoolean());
    }

    [Fact]
    public void Batch_PreservesPartialContentAndReportsErrorsPerMember()
    {
        string[] members = ["T:TestLibrary.SimpleClass", "T:Missing.Type"];
        var compact = Data(BatchGetDecompiledSourceTool.BatchGetDecompiledSource(members));
        var extended = Data(Extended(() => BatchGetDecompiledSourceTool.BatchGetDecompiledSource(members)));
        var item = compact.GetProperty("items")[0];
        var slice = extended.GetProperty("items")[0].GetProperty("firstSlice");

        foreach (var field in new[] { "memberId", "startLine", "endLine", "totalLines", "code" })
            Assert.Equal(slice.GetProperty(field).ToString(), item.GetProperty(field).ToString());
        Assert.Equal(50, item.GetProperty("endLine").GetInt32());
        Assert.True(item.GetProperty("totalLines").GetInt32() > 50);
        Assert.True(item.TryGetProperty("sourceKind", out _));
        var failure = compact.GetProperty("items")[1];
        Assert.Equal(members[1], failure.GetProperty("memberId").GetString());
        Assert.True(failure.TryGetProperty("error", out _));
        Assert.False(failure.TryGetProperty("code", out _));

        var capped = Data(BatchGetDecompiledSourceTool.BatchGetDecompiledSource([members[0], members[0]],
            maxTotalChars: slice.GetProperty("code").GetString()!.Length));
        Assert.True(capped.GetProperty("truncated").GetBoolean());
        Assert.Single(capped.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void Json_PreservesLiteralOperatorsAndEscapesControlCharacters()
    {
        const string code = "a < b && c > d + e;\n\t\"quoted\" \\ path";
        var json = ResponseFormatter.Success(new { code });
        Assert.Contains("a < b && c > d + e;", json);
        Assert.DoesNotContain("\\u003C", json);
        Assert.Equal(code, Data(json).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("compact", false)]
    [InlineData(" EXTENDED ", true)]
    public void StartupFormat_DefaultsToCompact(string? setting, bool extended) =>
        Assert.Equal(extended, ResponseFormatOptions.Parse(setting).Extended);

    [Fact]
    public void StartupFormat_RejectsUnknownMode() =>
        Assert.Throws<ArgumentException>(() => ResponseFormatOptions.Parse("typo"));

    public new void Dispose()
    {
        ServiceLocator.SetServiceProvider(null);
        _provider.Dispose();
        base.Dispose();
    }
}

[Collection(ServiceLocatorIsolationCollection.Name)]
public sealed class ResponseFormatProtocolTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("extended", true)]
    public async Task Stdio_UsesUserStartupFormatWithoutToolArguments(string? setting, bool extended)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [typeof(DecompilerServer.Program).Assembly.Location],
            EnvironmentVariables = new Dictionary<string, string?> { [ResponseFormatOptions.EnvironmentVariable] = setting }
        }), cancellationToken: timeout.Token);
        // Invalid batch input fails before routing or activating any registered assembly.
        var result = await client.CallToolAsync("batch_get_decompiled_source",
            new Dictionary<string, object?> { ["memberIds"] = Array.Empty<string>() }, cancellationToken: timeout.Token);
        var response = JsonSerializer.Deserialize<JsonElement>(Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        Assert.Equal("error", response.GetProperty("status").GetString());
        Assert.True(response.TryGetProperty("error", out _));
        Assert.Equal(extended, response.TryGetProperty("message", out _));
        Assert.Equal(extended, response.TryGetProperty("details", out _));
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.All(tools, tool =>
        {
            var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
            Assert.False(properties.TryGetProperty("compact", out _));
            Assert.False(properties.TryGetProperty("responseMode", out _));
        });
    }

}

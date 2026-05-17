using ICSharpCode.Decompiler.TypeSystem;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DecompilerServer.Services;

/// <summary>
/// Analyzes IL code to find usage patterns, callers, and callees.
/// Provides functionality for finding where members are used across the assembly with caching.
/// </summary>
public class UsageAnalyzer
{
    private readonly AssemblyContextManager _contextManager;
    private readonly MemberResolver _memberResolver;
    private readonly ConcurrentDictionary<string, List<UsageReference>> _usageCache = new();
    private readonly ConcurrentDictionary<string, List<StringLiteralReference>> _stringLiteralCache = new();
    private readonly object _cacheLock = new();
    private long _cacheVersion = -1;

    private static readonly OpCode[] _singleByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] _multiByteOpCodes = new OpCode[0x100];

    static UsageAnalyzer()
    {
        foreach (var fi in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var op = (OpCode)fi.GetValue(null)!;
            var value = (ushort)op.Value;
            if (value < 0x100)
                _singleByteOpCodes[value] = op;
            else if ((value & 0xff00) == 0xfe00)
                _multiByteOpCodes[value & 0xff] = op;
        }
    }

    public UsageAnalyzer(AssemblyContextManager contextManager, MemberResolver memberResolver)
    {
        _contextManager = contextManager;
        _memberResolver = memberResolver;
    }

    /// <summary>
    /// Find all usages of a member across the assembly (cached)
    /// </summary>
    public IEnumerable<UsageReference> FindUsages(string memberId, int limit = 100, string? cursor = null)
    {
        return FindUsagesPage(memberId, limit, cursor).Items;
    }

    public AnalysisPage<UsageReference> FindUsagesPage(string memberId, int limit = 100, string? cursor = null)
    {
        EnsureCacheCurrent();

        var allUsages = _usageCache.GetOrAdd(memberId, _ => CollectUsages(memberId));
        return Page(allUsages, limit, cursor);
    }

    private List<UsageReference> CollectUsages(string memberId)
    {
        var targetMember = _memberResolver.ResolveMember(memberId);
        if (targetMember == null)
            return new List<UsageReference>();

        var usages = new List<UsageReference>();
        var allTypes = _contextManager.GetAllTypes();

        foreach (var type in allTypes)
        {
            foreach (var method in type.Methods)
            {
                usages.AddRange(FindUsagesInMethod(method, targetMember));
            }
        }

        return usages;
    }

    /// <summary>
    /// Find direct callers of a method
    /// </summary>
    public IEnumerable<UsageReference> FindCallers(string methodId, int limit = 100, string? cursor = null)
    {
        return FindCallersPage(methodId, limit, cursor).Items;
    }

    public AnalysisPage<UsageReference> FindCallersPage(string methodId, int limit = 100, string? cursor = null)
    {
        EnsureCacheCurrent();
        var allCallers = _usageCache
            .GetOrAdd(methodId, _ => CollectUsages(methodId))
            .Where(u => u.Kind == UsageKind.Call || u.Kind == UsageKind.NewObject)
            .ToList();

        return Page(allCallers, limit, cursor);
    }

    /// <summary>
    /// Find what methods/members a method calls (callees)
    /// </summary>
    public IEnumerable<CalleeReference> FindCallees(string methodId, int limit = 100, string? cursor = null)
    {
        return FindCalleesPage(methodId, limit, cursor).Items;
    }

    public AnalysisPage<CalleeReference> FindCalleesPage(string methodId, int limit = 100, string? cursor = null)
    {
        EnsureCacheCurrent();

        var method = _memberResolver.ResolveMethod(methodId);
        if (method == null)
            return AnalysisPage<CalleeReference>.Empty();

        var peFile = _contextManager.GetPEFile();
        var metadata = peFile.Metadata;
        var body = IlAnalysisService.ReadMethodBody(method, _contextManager);
        var results = new List<CalleeReference>();
        if (!body.HasBody)
            return AnalysisPage<CalleeReference>.Empty();

        foreach (var instruction in body.Instructions)
        {
            var kind = GetCalleeKind(instruction.OpCode);
            if (kind == null)
                continue;

            results.Add(CreateCalleeReference(metadata, instruction, kind));
        }

        return Page(results, limit, cursor);
    }

    /// <summary>
    /// Find string literals in a method
    /// </summary>
    public IEnumerable<string> FindStringLiteralsInMethod(IMethod method)
    {
        EnsureCacheCurrent();

        if (method.MetadataToken.IsNil)
            return Enumerable.Empty<string>();

        var peFile = _contextManager.GetPEFile();
        var metadata = peFile.Metadata;
        if (method.MetadataToken.Kind != HandleKind.MethodDefinition)
            return Enumerable.Empty<string>();

        var methodDef = metadata.GetMethodDefinition((MethodDefinitionHandle)method.MetadataToken);
        if (methodDef.RelativeVirtualAddress == 0)
            return Enumerable.Empty<string>();
        var body = peFile.Reader.GetMethodBody(methodDef.RelativeVirtualAddress);

        var literals = new List<string>();
        var il = body.GetILBytes();
        if (il == null || il.Length == 0)
            return literals;

        foreach (var (opCode, operand) in ReadInstructions(il))
        {
            if (opCode == OpCodes.Ldstr)
            {
                var strHandle = MetadataTokens.UserStringHandle(operand);
                var value = metadata.GetUserString(strHandle);
                literals.Add(value);
            }
        }

        return literals;
    }

    /// <summary>
    /// Find all string literals in the assembly (cached)
    /// </summary>
    public IEnumerable<StringLiteralReference> FindStringLiterals(string query, bool regex = false, int limit = 100, string? cursor = null)
    {
        return FindStringLiteralsPage(query, regex, limit, cursor).Items;
    }

    public AnalysisPage<StringLiteralReference> FindStringLiteralsPage(string query, bool regex = false, int limit = 100, string? cursor = null)
    {
        EnsureCacheCurrent();
        ValidateRegex(query, regex);

        var cacheKey = $"{query}:{regex}";

        var allLiterals = _stringLiteralCache.GetOrAdd(cacheKey, _ => CollectStringLiterals(query, regex));
        return Page(allLiterals, limit, cursor);
    }

    private List<StringLiteralReference> CollectStringLiterals(string query, bool regex)
    {
        var results = new List<StringLiteralReference>();
        var allTypes = _contextManager.GetAllTypes();

        foreach (var type in allTypes)
        {
            foreach (var method in type.Methods)
            {
                var literals = FindStringLiteralsInMethod(method);
                foreach (var literal in literals)
                {
                    if (MatchesStringQuery(literal, query, regex))
                    {
                        results.Add(new StringLiteralReference(
                              literal,
                              _memberResolver.GenerateMemberId(method),
                              method.DeclaringType?.FullName ?? "",
                              null)); // Would need source mapping for line numbers
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Clear usage analysis caches
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _usageCache.Clear();
            _stringLiteralCache.Clear();
            _cacheVersion = _contextManager.ContextVersion;
        }
    }

    /// <summary>
    /// Get usage analysis cache statistics
    /// </summary>
    public UsageAnalyzerCacheStats GetCacheStats()
    {
        EnsureCacheCurrent();

        return new UsageAnalyzerCacheStats(
              _usageCache.Count,
              _stringLiteralCache.Count,
              _usageCache.Values.Sum(list => list.Count),
              _stringLiteralCache.Values.Sum(list => list.Count));
    }

    private void EnsureCacheCurrent()
    {
        var version = _contextManager.ContextVersion;
        if (_cacheVersion == version)
            return;

        lock (_cacheLock)
        {
            if (_cacheVersion == version)
                return;

            _usageCache.Clear();
            _stringLiteralCache.Clear();
            _cacheVersion = version;
        }
    }

    private IEnumerable<UsageReference> FindUsagesInMethod(IMethod method, IEntity targetMember)
    {
        if (method.MetadataToken.IsNil)
            return Enumerable.Empty<UsageReference>();

        var peFile = _contextManager.GetPEFile();
        var metadata = peFile.Metadata;
        if (method.MetadataToken.Kind != HandleKind.MethodDefinition)
            return Enumerable.Empty<UsageReference>();

        var methodDef = metadata.GetMethodDefinition((MethodDefinitionHandle)method.MetadataToken);
        if (methodDef.RelativeVirtualAddress == 0)
            return Enumerable.Empty<UsageReference>();
        var body = peFile.Reader.GetMethodBody(methodDef.RelativeVirtualAddress);

        var usages = new List<UsageReference>();
        var il = body.GetILBytes();
        if (il == null || il.Length == 0)
            return usages;

        var targetToken = MetadataTokens.GetToken(targetMember.MetadataToken);

        foreach (var (opCode, operand) in ReadInstructions(il))
        {
            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj ||
                  opCode == OpCodes.Ldfld || opCode == OpCodes.Ldsfld || opCode == OpCodes.Stfld || opCode == OpCodes.Stsfld)
            {
                if (operand == targetToken)
                {
                    var kind = opCode == OpCodes.Newobj ? UsageKind.NewObject :
                          (opCode == OpCodes.Ldfld || opCode == OpCodes.Ldsfld ? UsageKind.FieldRead :
                            (opCode == OpCodes.Stfld || opCode == OpCodes.Stsfld ? UsageKind.FieldWrite : UsageKind.Call));
                    usages.Add(new UsageReference
                    {
                        InMember = _memberResolver.GenerateMemberId(method),
                        InType = method.DeclaringType?.FullName ?? "",
                        Kind = kind
                    });
                }
            }
        }

        return usages;
    }

    private static string? GetCalleeKind(string opcode)
    {
        return opcode switch
        {
            "call" or "callvirt" => nameof(UsageKind.Call),
            "newobj" => nameof(UsageKind.NewObject),
            "ldfld" or "ldsfld" => nameof(UsageKind.FieldRead),
            "stfld" or "stsfld" => nameof(UsageKind.FieldWrite),
            _ => null
        };
    }

    private CalleeReference CreateCalleeReference(MetadataReader metadata, IlInstructionInfo instruction, string kind)
    {
        var target = instruction.OperandToken.HasValue
            ? ResolveCalleeTarget(metadata, instruction.OperandToken.Value)
            : null;

        var symbol = target?.Symbol
            ?? instruction.OperandSummary
            ?? instruction.OperandTokenHex
            ?? instruction.Operand?.ToString()
            ?? "unknown";

        return new CalleeReference
        {
            TargetMemberId = target?.MemberId,
            Symbol = symbol,
            DeclaringType = target?.DeclaringType,
            Kind = kind,
            Opcode = instruction.OpCode,
            Offset = instruction.Offset,
            Resolved = target?.MemberId != null,
            Resolution = target?.Resolution ?? "unresolved",
            OperandToken = instruction.OperandToken,
            OperandTokenHex = instruction.OperandTokenHex
        };
    }

    private CalleeTarget? ResolveCalleeTarget(MetadataReader metadata, int token)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.MethodDefinition or HandleKind.FieldDefinition => ResolveLocalTarget(handle, metadata, token),
                HandleKind.MemberReference => ResolveMemberReferenceTarget(metadata, (MemberReferenceHandle)handle),
                _ => new CalleeTarget(null, $"0x{token:X8}", null, "unresolved")
            };
        }
        catch
        {
            return new CalleeTarget(null, $"0x{token:X8}", null, "unresolved");
        }
    }

    private CalleeTarget ResolveLocalTarget(EntityHandle handle, MetadataReader metadata, int token)
    {
        var entity = ResolveEntity(handle);
        if (entity != null)
        {
            var declaringType = entity is IMember member
                ? member.DeclaringType?.FullName
                : (entity as ITypeDefinition)?.FullName;
            return new CalleeTarget(
                _memberResolver.GenerateMemberId(entity),
                entity.FullName,
                declaringType,
                "assembly");
        }

        return handle.Kind switch
        {
            HandleKind.MethodDefinition => ResolveMethodDefinitionTarget(metadata, (MethodDefinitionHandle)handle, token),
            HandleKind.FieldDefinition => ResolveFieldDefinitionTarget(metadata, (FieldDefinitionHandle)handle, token),
            _ => new CalleeTarget(null, $"0x{token:X8}", null, "unresolved")
        };
    }

    private CalleeTarget ResolveMethodDefinitionTarget(MetadataReader metadata, MethodDefinitionHandle handle, int token)
    {
        var methodDefinition = metadata.GetMethodDefinition(handle);
        var declaringType = GetFullTypeName(metadata, methodDefinition.GetDeclaringType());
        var name = metadata.GetString(methodDefinition.Name);
        var symbol = declaringType == null ? name : $"{declaringType}.{name}";
        return new CalleeTarget(null, symbol, declaringType, "unresolved");
    }

    private CalleeTarget ResolveFieldDefinitionTarget(MetadataReader metadata, FieldDefinitionHandle handle, int token)
    {
        var fieldDefinition = metadata.GetFieldDefinition(handle);
        var declaringType = GetFullTypeName(metadata, fieldDefinition.GetDeclaringType());
        var name = metadata.GetString(fieldDefinition.Name);
        var symbol = declaringType == null ? name : $"{declaringType}.{name}";
        return new CalleeTarget(null, symbol, declaringType, "unresolved");
    }

    private CalleeTarget ResolveMemberReferenceTarget(MetadataReader metadata, MemberReferenceHandle handle)
    {
        var memberReference = metadata.GetMemberReference(handle);
        var declaringType = GetFullTypeName(metadata, memberReference.Parent);
        var name = metadata.GetString(memberReference.Name);
        var symbol = declaringType == null ? name : $"{declaringType}.{name}";
        return new CalleeTarget(null, symbol, declaringType, "external");
    }

    private IEntity? ResolveEntity(EntityHandle handle)
    {
        try
        {
            if (_contextManager.GetCompilation().MainModule is ICSharpCode.Decompiler.TypeSystem.MetadataModule module)
                return module.ResolveEntity(handle);
        }
        catch
        {
            return null;
        }

        return null;
    }

    private string? GetFullTypeName(MetadataReader metadata, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                var td = metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
                var ns = metadata.GetString(td.Namespace);
                var name = metadata.GetString(td.Name);
                return string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
            case HandleKind.TypeReference:
                var tr = metadata.GetTypeReference((TypeReferenceHandle)handle);
                var ns2 = metadata.GetString(tr.Namespace);
                var name2 = metadata.GetString(tr.Name);
                return string.IsNullOrEmpty(ns2) ? name2 : $"{ns2}.{name2}";
            case HandleKind.MethodDefinition:
                var methodDefinition = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return GetFullTypeName(metadata, methodDefinition.GetDeclaringType());
            case HandleKind.MemberReference:
                var memberReference = metadata.GetMemberReference((MemberReferenceHandle)handle);
                return GetFullTypeName(metadata, memberReference.Parent);
            default:
                return null;
        }
    }

    private static IEnumerable<(OpCode opCode, int operand)> ReadInstructions(byte[] il)
    {
        var pos = 0;
        while (pos < il.Length)
        {
            OpCode opCode;
            var code = il[pos++];
            if (code == 0xFE)
            {
                opCode = _multiByteOpCodes[il[pos++]];
            }
            else
            {
                opCode = _singleByteOpCodes[code];
            }

            int operand = 0;
            switch (opCode.OperandType)
            {
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    operand = BitConverter.ToInt32(il, pos);
                    pos += 4;
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    operand = il[pos];
                    pos += 1;
                    break;
                case OperandType.InlineVar:
                    operand = BitConverter.ToUInt16(il, pos);
                    pos += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    pos += 8;
                    break;
                case OperandType.ShortInlineR:
                    pos += 4;
                    break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, pos);
                    pos += 4 + 4 * count;
                    break;
                case OperandType.InlineNone:
                default:
                    break;
            }

            yield return (opCode, operand);
        }
    }

    private bool MatchesStringQuery(string text, string query, bool regex)
    {
        if (string.IsNullOrEmpty(query))
            return true;

        if (regex)
        {
            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(text, query,
                      System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                      TimeSpan.FromSeconds(1));
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                return false;
            }
        }

        return text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateRegex(string query, bool regex)
    {
        if (!regex || string.IsNullOrEmpty(query))
            return;

        try
        {
            _ = new System.Text.RegularExpressions.Regex(
                query,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid regex pattern '{query}': {ex.Message}", nameof(query), ex);
        }
    }

    private static AnalysisPage<T> Page<T>(IReadOnlyList<T> items, int limit, string? cursor)
    {
        var normalizedLimit = limit <= 0 ? 100 : Math.Min(limit, 100);
        var startIndex = ParseCursor(cursor);

        var pageItems = items.Skip(startIndex).Take(normalizedLimit).ToList();
        var hasMore = startIndex + normalizedLimit < items.Count;
        return new AnalysisPage<T>(
            pageItems,
            hasMore,
            hasMore ? (startIndex + normalizedLimit).ToString() : null,
            items.Count);
    }

    private static int ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;

        if (!int.TryParse(cursor, out var startIndex) || startIndex < 0)
            throw new ArgumentException("cursor must be a non-negative integer.", nameof(cursor));

        return startIndex;
    }
}

public sealed record AnalysisPage<T>(List<T> Items, bool HasMore, string? NextCursor, int TotalEstimate)
{
    public static AnalysisPage<T> Empty()
    {
        return new AnalysisPage<T>(new List<T>(), false, null, 0);
    }
}

/// <summary>
/// Represents a usage of a member
/// </summary>
public record UsageReference
{
    public required string InMember { get; init; }
    public required string InType { get; init; }
    public required UsageKind Kind { get; init; }
    public int? Line { get; init; }
    public string? Snippet { get; init; }
}

/// <summary>
/// Represents a direct callee referenced by a method body.
/// </summary>
public record CalleeReference
{
    public string? TargetMemberId { get; init; }
    public required string Symbol { get; init; }
    public string? DeclaringType { get; init; }
    public required string Kind { get; init; }
    public required string Opcode { get; init; }
    public required int Offset { get; init; }
    public required bool Resolved { get; init; }
    public required string Resolution { get; init; }
    public int? OperandToken { get; init; }
    public string? OperandTokenHex { get; init; }

    // Compatibility aliases for older callers that consumed find_callees as UsageReference-shaped output.
    public string InMember => TargetMemberId ?? Symbol;
    public string? InType => DeclaringType;
}

internal sealed record CalleeTarget(
    string? MemberId,
    string Symbol,
    string? DeclaringType,
    string Resolution);

/// <summary>
/// Types of member usage
/// </summary>
public enum UsageKind
{
    Call,
    FieldRead,
    FieldWrite,
    PropertyRead,
    PropertyWrite,
    NewObject,
    TypeReference
}

/// <summary>
/// Represents a string literal reference
/// </summary>
public record StringLiteralReference(string Value, string ContainingMember, string ContainingType, int? Line);

/// <summary>
/// Usage analyzer cache statistics
/// </summary>
public record UsageAnalyzerCacheStats(int CachedUsageQueries, int CachedStringLiteralQueries, int TotalUsageResults,
      int TotalStringLiteralResults);

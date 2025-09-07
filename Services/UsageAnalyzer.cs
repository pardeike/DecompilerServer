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
        // Create cache key
        var cacheKey = $"{memberId}:{limit}:{cursor}";

        // Check cache first
        if (_usageCache.TryGetValue(cacheKey, out var cachedUsages))
            return cachedUsages.Take(limit);

        var targetMember = _memberResolver.ResolveMember(memberId);
        if (targetMember == null)
            return Enumerable.Empty<UsageReference>();

        var usages = new List<UsageReference>();
        var compilation = _contextManager.GetCompilation();
        var allTypes = _contextManager.GetAllTypes();

        // Parse cursor for pagination
        var startIndex = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var processedCount = 0;
        var foundCount = 0;

        foreach (var type in allTypes)
        {
            if (foundCount >= limit) break;

            foreach (var method in type.Methods)
            {
                processedCount++;
                if (processedCount <= startIndex) continue;
                if (foundCount >= limit) break;

                var methodUsages = FindUsagesInMethod(method, targetMember);
                foreach (var usage in methodUsages)
                {
                    if (foundCount >= limit) break;
                    usages.Add(usage);
                    foundCount++;
                }
            }
        }

        // Cache the result
        _usageCache.TryAdd(cacheKey, usages);

        return usages;
    }

    /// <summary>
    /// Find direct callers of a method
    /// </summary>
    public IEnumerable<UsageReference> FindCallers(string methodId, int limit = 100, string? cursor = null)
    {
        var usages = FindUsages(methodId, limit, cursor);
        return usages.Where(u => u.Kind == UsageKind.Call || u.Kind == UsageKind.NewObject);
    }

    /// <summary>
    /// Find what methods/members a method calls (callees)
    /// </summary>
    public IEnumerable<UsageReference> FindCallees(string methodId, int limit = 100, string? cursor = null)
    {
        var method = _memberResolver.ResolveMethod(methodId);
        if (method == null)
            return Enumerable.Empty<UsageReference>();
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
        var il = body.GetILBytes();

        var results = new List<UsageReference>();
        var startIndex = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
            startIndex = cursorIndex;
        var index = 0;

        foreach (var (opCode, operand) in ReadInstructions(il))
        {
            if (results.Count >= limit)
                break;

            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj)
            {
                if (index++ < startIndex) continue;
                var resolved = ResolveMethodId(metadata, operand);
                if (resolved != null)
                {
                    results.Add(new UsageReference
                    {
                        InMember = resolved.Value.memberId,
                        InType = resolved.Value.typeName,
                        Kind = opCode == OpCodes.Newobj ? UsageKind.NewObject : UsageKind.Call
                    });
                }
            }
            else if (opCode == OpCodes.Ldfld || opCode == OpCodes.Ldsfld || opCode == OpCodes.Stfld || opCode == OpCodes.Stsfld)
            {
                if (index++ < startIndex) continue;
                var field = ResolveFieldId(metadata, operand);
                if (field != null)
                {
                    var kind = opCode == OpCodes.Ldfld || opCode == OpCodes.Ldsfld
                        ? UsageKind.FieldRead
                        : UsageKind.FieldWrite;
                    results.Add(new UsageReference
                    {
                        InMember = field.Value.memberId,
                        InType = field.Value.typeName,
                        Kind = kind
                    });
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Find string literals in a method
    /// </summary>
    public IEnumerable<string> FindStringLiteralsInMethod(IMethod method)
    {
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
        var il = body.GetILBytes();

        var literals = new List<string>();
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
        // Create cache key
        var cacheKey = $"{query}:{regex}:{limit}:{cursor}";

        // Check cache first
        if (_stringLiteralCache.TryGetValue(cacheKey, out var cachedLiterals))
            return cachedLiterals.Take(limit);

        var results = new List<StringLiteralReference>();
        var compilation = _contextManager.GetCompilation();
        var allTypes = _contextManager.GetAllTypes();

        // Parse cursor for pagination
        var startIndex = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var processedCount = 0;
        var foundCount = 0;

        foreach (var type in allTypes)
        {
            if (foundCount >= limit) break;

            foreach (var method in type.Methods)
            {
                processedCount++;
                if (processedCount <= startIndex) continue;
                if (foundCount >= limit) break;

                var literals = FindStringLiteralsInMethod(method);
                foreach (var literal in literals)
                {
                    if (foundCount >= limit) break;

                    if (MatchesStringQuery(literal, query, regex))
                    {
                        results.Add(new StringLiteralReference(
                            literal,
                            _memberResolver.GenerateMemberId(method),
                            method.DeclaringType?.FullName ?? "",
                            null)); // Would need source mapping for line numbers
                        foundCount++;
                    }
                }
            }
        }

        // Cache the result
        _stringLiteralCache.TryAdd(cacheKey, results);

        return results;
    }

    /// <summary>
    /// Clear usage analysis caches
    /// </summary>
    public void ClearCache()
    {
        _usageCache.Clear();
        _stringLiteralCache.Clear();
    }

    /// <summary>
    /// Get usage analysis cache statistics
    /// </summary>
    public UsageAnalyzerCacheStats GetCacheStats()
    {
        return new UsageAnalyzerCacheStats(
            _usageCache.Count,
            _stringLiteralCache.Count,
            _usageCache.Values.Sum(list => list.Count),
            _stringLiteralCache.Values.Sum(list => list.Count));
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
        var il = body.GetILBytes();

        var usages = new List<UsageReference>();
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

    private (string memberId, string typeName)? ResolveMethodId(MetadataReader metadata, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                var md = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                var typeName = GetFullTypeName(metadata, md.GetDeclaringType());
                var name = metadata.GetString(md.Name);
                return ($"M:{typeName}.{name}", typeName);
            case HandleKind.MemberReference:
                var mr = metadata.GetMemberReference((MemberReferenceHandle)handle);
                var parentName = GetFullTypeName(metadata, mr.Parent);
                if (parentName == null) return null;
                var methodName = metadata.GetString(mr.Name);
                return ($"M:{parentName}.{methodName}", parentName);
            default:
                return null;
        }
    }

    private (string memberId, string typeName)? ResolveFieldId(MetadataReader metadata, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        switch (handle.Kind)
        {
            case HandleKind.FieldDefinition:
                var fd = metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
                var typeName = GetFullTypeName(metadata, fd.GetDeclaringType());
                var name = metadata.GetString(fd.Name);
                return ($"F:{typeName}.{name}", typeName);
            case HandleKind.MemberReference:
                var mr = metadata.GetMemberReference((MemberReferenceHandle)handle);
                var parentName = GetFullTypeName(metadata, mr.Parent);
                if (parentName == null) return null;
                var fieldName = metadata.GetString(mr.Name);
                return ($"F:{parentName}.{fieldName}", parentName);
            default:
                return null;
        }
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
            catch
            {
                return false;
            }
        }

        return text.Contains(query, StringComparison.OrdinalIgnoreCase);
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
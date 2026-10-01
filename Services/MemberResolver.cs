using ICSharpCode.Decompiler.TypeSystem;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System;
using ICSharpCode.Decompiler.Documentation;

namespace DecompilerServer.Services;

/// <summary>
/// Resolves member IDs to IEntity objects and handles member ID normalization.
/// Supports various member ID formats and provides consistent resolution with caching.
/// </summary>
public class MemberResolver
{
    private readonly AssemblyContextManager _contextManager;
    private readonly BoundedCache<string, IEntity?> _resolutionCache = new(4096, 2 * 1024 * 1024, _ => 256);
    private readonly BoundedCache<string, string> _signatureCache = new(100_000, 16 * 1024 * 1024, value => 128 + value.Length * 2L);
    private readonly object _cacheLock = new();
    private long _cacheVersion = -1;
    private readonly ConcurrentDictionary<string, string> _dependencyPaths = new(StringComparer.OrdinalIgnoreCase);

    public MemberResolver(AssemblyContextManager contextManager)
    {
        _contextManager = contextManager;
    }

    /// <summary>
    /// Resolve a member ID to an IEntity (type, method, field, property, event) with caching
    /// </summary>
    public IEntity? ResolveMember(string memberId)
    {
        EnsureCacheCurrent();

        if (string.IsNullOrWhiteSpace(memberId))
            return null;

        if (Regex.IsMatch(memberId, @"^[0-9a-fA-F]{32}:00000000:[TMPFE]$"))
            throw new ToolErrorException("invalid_member_id", "A zero metadata token does not identify a real member.", new { input = memberId });

        if (TryGetMvid(memberId, out var mvid) && !string.Equals(mvid, _contextManager.Mvid?.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
            throw CreateContextError(memberId, mvid);

        // Check cache first
        if (_resolutionCache.TryGetValue(memberId, out var cached))
            return cached;

        var compilation = _contextManager.GetCompilation();

        // Try fast path with indexed members first
        var entity = _contextManager.FindMemberById(memberId) ??
                     ResolveMemberByHumanQualifiedName(memberId, compilation) ??
                     ResolveMemberByTokenId(memberId, compilation) ??
                     ResolveMemberByMetadataToken(memberId, compilation);

        if (entity?.MetadataToken.IsNil == true)
            entity = null;

        // Cache the result (including null results to avoid repeated failed lookups)
        _resolutionCache.TryAdd(memberId, entity);

        return entity;
    }

    /// <summary>
    /// Resolve a member ID specifically to a type
    /// </summary>
    public IType? ResolveType(string typeId)
    {
        var entity = ResolveMember(typeId);
        return entity as IType ?? (entity as IMember)?.DeclaringType;
    }

    /// <summary>
    /// Resolve a member ID specifically to a method
    /// </summary>
    public IMethod? ResolveMethod(string methodId)
    {
        return ResolveMember(methodId) as IMethod;
    }

    /// <summary>
    /// Resolve a member ID specifically to a field
    /// </summary>
    public IField? ResolveField(string fieldId)
    {
        return ResolveMember(fieldId) as IField;
    }

    /// <summary>
    /// Resolve a member ID specifically to a property
    /// </summary>
    public IProperty? ResolveProperty(string propertyId)
    {
        return ResolveMember(propertyId) as IProperty;
    }

    /// <summary>
    /// Normalize a member ID to a consistent format
    /// </summary>
    public string NormalizeMemberId(string memberId)
    {
        var entity = ResolveMember(memberId);
        if (entity == null)
            return memberId; // Return original if can't resolve

        return GenerateMemberId(entity);
    }

    /// <summary>
    /// Generate a consistent member ID from an IEntity
    /// </summary>
    public string GenerateMemberId(IEntity entity)
    {
        if (entity.MetadataToken.IsNil)
            throw new ToolErrorException("invalid_member_id", "Synthetic members without metadata tokens cannot be addressed.");

        var metadata = entity.ParentModule?.MetadataFile?.Metadata;
        var mvid = metadata != null
            ? metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("N")
            : _contextManager.Mvid?.Replace("-", "") ?? new string('0', 32);

        if (!string.Equals(mvid, _contextManager.Mvid?.Replace("-", ""), StringComparison.OrdinalIgnoreCase)
            && entity.ParentModule?.MetadataFile?.FileName is { } path)
            _dependencyPaths.TryAdd(mvid, path);

        var token = MetadataTokens.GetToken(entity.MetadataToken);
        var kind = entity switch
        {
            ITypeDefinition => 'T',
            IMethod => 'M',
            IField => 'F',
            IProperty => 'P',
            IEvent => 'E',
            _ => 'T'
        };

        return $"{mvid}:{token:X8}:{kind}";
    }

    internal static bool TryGetMvid(string input, out string mvid)
    {
        var match = Regex.Match(input, @"^([0-9a-fA-F]{32}):[0-9a-fA-F]{8}:[TMPFE]$");
        mvid = match.Groups[1].Value;
        return match.Success;
    }

    internal string? GetDependencyPath(string mvid) => _dependencyPaths.GetValueOrDefault(mvid);

    internal ToolErrorException CreateContextError(string memberId, string mvid, string code = "context_not_loaded")
    {
        var path = GetDependencyPath(mvid);
        return new ToolErrorException(code, "The member belongs to a different assembly context.",
            new { memberId, mvid, assemblyPath = path },
            path == null ? [new ToolErrorHint("list_contexts", new { includeRegistered = true }, "Activate the owning assembly alias, then retry with its contextAlias.")]
                : [new ToolErrorHint("load_assembly", new { assemblyPath = path, makeCurrent = false, contextAlias = "dependency-" + mvid[..8] }, "Load the defining assembly, then retry the canonical memberId.")]);
    }

    internal void EnsureOwnedEntity(IEntity entity)
    {
        var id = GenerateMemberId(entity);
        if (TryGetMvid(id, out var mvid) && !string.Equals(mvid, _contextManager.Mvid?.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
            throw CreateContextError(id, mvid, "member_context_mismatch");
    }

    /// <summary>
    /// Get a human-readable signature for a member
    /// </summary>
    public string GetMemberSignature(IEntity entity)
    {
        return _signatureCache.GetOrAdd(GenerateMemberId(entity), _ => entity switch
        {
            IMethod method => FormatMethodSignature(method),
            IProperty property => FormatPropertySignature(property),
            IField field => FormatFieldSignature(field),
            IEvent evt => FormatEventSignature(evt),
            IType type => FormatTypeSignature(type),
            _ => entity.Name
        });
    }

    /// <summary>
    /// Check if a member ID is valid format
    /// </summary>
    public bool IsValidMemberId(string memberId)
    {
        if (string.IsNullOrWhiteSpace(memberId))
            return false;

        return Regex.IsMatch(memberId,
            @"^([0-9A-Fa-f]{32}:[0-9A-Fa-f]{8}:[TMFPE]|[TMFPE]:.*|0x[0-9A-Fa-f]+|\d+)$");
    }

    /// <summary>
    /// Clear the resolution cache
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _resolutionCache.Clear();
            _signatureCache.Clear();
            _cacheVersion = _contextManager.ContextVersion;
        }
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public ResolverCacheStats GetCacheStats()
    {
        EnsureCacheCurrent();

        return new ResolverCacheStats(
            _resolutionCache.Count,
            _resolutionCache.Values.Count(value => value != null),
            _resolutionCache.Values.Count(value => value == null), _resolutionCache.Bytes + _signatureCache.Bytes);
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

            _resolutionCache.Clear();
            _signatureCache.Clear();
            _cacheVersion = version;
        }
    }

    private IEntity? ResolveMemberByTokenId(string memberId, ICompilation compilation)
    {
        // Handle hex tokens like "0x06000123"
        if (!memberId.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!int.TryParse(memberId.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out var token))
            return null;

        return ResolveMemberByToken(token, compilation);
    }

    private IEntity? ResolveMemberByHumanQualifiedName(string input, ICompilation compilation)
    {
        var value = input.Trim();
        char? kind = null;
        if (value.Length > 2 && value[1] == ':' && "TMFPE".Contains(value[0]))
        {
            kind = value[0];
            value = value[2..];
        }
        if (kind == 'T')
            return FindTypeDefinition(value, compilation);

        // ILSpy handles exact XML documentation signatures, including generic parameters and byrefs.
        if (kind == 'M' && value.Contains('('))
        {
            try
            {
                var exact = IdStringProvider.FindEntity(input, new SimpleTypeResolveContext(compilation.MainModule));
                if (exact != null)
                    return exact;
            }
            catch (ReflectionNameParseException) { }
        }
        string? parameters = null;
        var paren = value.IndexOf('(');
        if (paren >= 0)
        {
            if (!value.EndsWith(')'))
                return null;
            parameters = value[(paren + 1)..^1];
            value = value[..paren];
        }
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.Contains(':', StringComparison.Ordinal))
        {
            var parts = value.Split(':', 2);
            if (parts.Length == 2)
            {
                var member = ResolveMemberByTypeAndName(parts[0].Trim(), parts[1].Trim(), compilation, parameters, kind);
                if (member != null)
                    return member;
            }
        }

        for (var index = value.LastIndexOf('.'); index > 0; index = value.LastIndexOf('.', index - 1))
        {
            if (index >= value.Length - 1)
                continue;

            var member = ResolveMemberByTypeAndName(value[..index].Trim(), value[(index + 1)..].Trim(), compilation, parameters, kind);
            if (member != null)
                return member;
        }

        return kind == null && parameters == null ? FindTypeDefinition(value, compilation) : null;
    }

    private IEntity? ResolveMemberByTypeAndName(string typeName, string memberName, ICompilation compilation, string? parameters = null, char? kind = null)
    {
        var type = FindTypeDefinition(typeName, compilation);
        if (type == null)
            return null;

        var requested = memberName.Replace("#ctor", ".ctor");
        var matches = TypeSurfaceComparer.GetAddressableMembers(type)
            .Where(member => string.Equals(member.Name, requested, StringComparison.OrdinalIgnoreCase))
            .Where(member => kind == null || GetKind(member) == kind)
            .Where(member => parameters == null || member is IMethod method && ParametersMatch(method, parameters))
            .Cast<IEntity>().ToArray();
        if (matches.Length > 1)
            throw new ToolErrorException("ambiguous_member", "Specify the parameter types or use a canonical memberId.",
                new { input = typeName + "." + memberName, candidates = matches.Select(member => SymbolResolutionDiagnostics.CreateMemberSummary((IMember)member, this)).ToArray() });
        return matches.SingleOrDefault();
    }

    private static char GetKind(IMember member) => member switch { IMethod => 'M', IProperty => 'P', IField => 'F', IEvent => 'E', _ => '?' };

    private static bool ParametersMatch(IMethod method, string requested)
    {
        var id = IdStringProvider.GetIdString(method);
        var start = id.IndexOf('(');
        var actual = start < 0 ? "" : id[(start + 1)..^1];
        // Normalize C# primitive spellings without weakening namespace or overload identity.
        foreach (var (alias, full) in new[] { ("string", "System.String"), ("int", "System.Int32"), ("bool", "System.Boolean"), ("long", "System.Int64"), ("double", "System.Double"), ("float", "System.Single"), ("object", "System.Object"), ("byte", "System.Byte"), ("char", "System.Char"), ("short", "System.Int16"), ("decimal", "System.Decimal"), ("uint", "System.UInt32"), ("ulong", "System.UInt64"), ("ushort", "System.UInt16"), ("sbyte", "System.SByte") })
            requested = Regex.Replace(requested, $@"(?<![\w.]){alias}(?!\w)", full);
        return string.Equals(actual, requested.Replace(" ", ""), StringComparison.Ordinal);
    }

    private ITypeDefinition? FindTypeDefinition(string typeName, ICompilation compilation)
    {
        var direct = _contextManager.FindTypeByName(typeName);
        if (direct != null)
            return direct;

        var type = compilation.FindType(new ICSharpCode.Decompiler.TypeSystem.FullTypeName(typeName)).GetDefinition();
        if (type != null)
            return type;

        return _contextManager.GetAllTypes().FirstOrDefault(candidate =>
            string.Equals(candidate.FullName, typeName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.ReflectionName, typeName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Name, typeName, StringComparison.OrdinalIgnoreCase)
            || candidate.FullName.EndsWith("." + typeName, StringComparison.OrdinalIgnoreCase)
            || candidate.ReflectionName.EndsWith("+" + typeName, StringComparison.OrdinalIgnoreCase));
    }

    private IEntity? ResolveMemberByMetadataToken(string memberId, ICompilation compilation)
    {
        // Handle decimal tokens
        if (!int.TryParse(memberId, out var token))
            return null;

        return ResolveMemberByToken(token, compilation);
    }

    private IEntity? ResolveMemberByToken(int token, ICompilation compilation)
    {
        try
        {
            var peFile = _contextManager.GetPEFile();
            _ = peFile.Metadata; // ensure metadata is loaded

            if (compilation.MainModule is not ICSharpCode.Decompiler.TypeSystem.MetadataModule module)
                return null;

            var handle = MetadataTokens.EntityHandle(token);
            return module.ResolveEntity(handle);
        }
        catch
        {
            return null;
        }
    }

    private string FormatMethodSignature(IMethod method)
    {
        var builder = CreateSignatureBuilder();
        var parameters = string.Join(", ", method.Parameters.Select(p => builder.ConvertParameter(p).ToString()));
        var typeParameters = method.TypeParameters.Count == 0 ? "" : "<" + string.Join(", ", method.TypeParameters.Select(p => p.Name)) + ">";
        return $"{FormatType(method.ReturnType)} {method.Name}{typeParameters}({parameters})";
    }

    private string FormatPropertySignature(IProperty property)
    {
        return $"{FormatType(property.ReturnType)} {property.Name}";
    }

    private string FormatFieldSignature(IField field)
    {
        return $"{FormatType(field.ReturnType)} {field.Name}";
    }

    private string FormatEventSignature(IEvent evt)
    {
        return $"event {FormatType(evt.ReturnType)} {evt.Name}";
    }

    internal static string FormatType(IType type) => CreateSignatureBuilder().ConvertType(type).ToString();

    private static ICSharpCode.Decompiler.CSharp.Syntax.TypeSystemAstBuilder CreateSignatureBuilder() => new()
    {
        AlwaysUseShortTypeNames = true,
        UseKeywordsForBuiltinTypes = false,
        ShowAttributes = false,
        ShowConstantValues = false
    };

    private string FormatTypeSignature(IType type)
    {
        return type.FullName;
    }
}

/// <summary>
/// Resolver cache statistics
/// </summary>
public record ResolverCacheStats(int CachedResolutions, int SuccessfulResolutions, int FailedResolutions, long EstimatedBytes = 0);

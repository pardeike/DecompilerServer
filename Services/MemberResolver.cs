using ICSharpCode.Decompiler.TypeSystem;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System;

namespace DecompilerServer.Services;

/// <summary>
/// Resolves member IDs to IEntity objects and handles member ID normalization.
/// Supports various member ID formats and provides consistent resolution with caching.
/// </summary>
public class MemberResolver
{
    private readonly AssemblyContextManager _contextManager;
    private readonly ConcurrentDictionary<string, IEntity?> _resolutionCache = new();
    private readonly object _cacheLock = new();
    private long _cacheVersion = -1;

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

        // Check cache first
        if (_resolutionCache.TryGetValue(memberId, out var cached))
            return cached;

        var compilation = _contextManager.GetCompilation();

        // Try fast path with indexed members first
        var entity = _contextManager.FindMemberById(memberId) ??
                     ResolveMemberByFullName(memberId, compilation) ??
                     ResolveMemberByHumanQualifiedName(memberId, compilation) ??
                     ResolveMemberByTokenId(memberId, compilation) ??
                     ResolveMemberByMetadataToken(memberId, compilation);

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
        var mvid = _contextManager.Mvid;
        if (mvid == null)
        {
            mvid = new string('0', 32);
        }
        else if (mvid.Contains('-'))
        {
            mvid = Guid.Parse(mvid).ToString("N");
        }

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

    /// <summary>
    /// Get a human-readable signature for a member
    /// </summary>
    public string GetMemberSignature(IEntity entity)
    {
        return entity switch
        {
            IMethod method => FormatMethodSignature(method),
            IProperty property => FormatPropertySignature(property),
            IField field => FormatFieldSignature(field),
            IEvent evt => FormatEventSignature(evt),
            IType type => FormatTypeSignature(type),
            _ => entity.Name
        };
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
            _resolutionCache.Count(kv => kv.Value != null),
            _resolutionCache.Count(kv => kv.Value == null));
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
            _cacheVersion = version;
        }
    }

    private IEntity? ResolveMemberByFullName(string memberId, ICompilation compilation)
    {
        // Handle XML documentation style IDs (T:, M:, F:, P:, E:)
        if (memberId.Length < 3 || memberId[1] != ':')
            return null;

        var prefix = memberId[0];
        var fullName = memberId.Substring(2);

        return prefix switch
        {
            'T' => compilation.FindType(new ICSharpCode.Decompiler.TypeSystem.FullTypeName(fullName)).GetDefinition(),
            'M' => FindMethodByFullName(fullName, compilation),
            'F' => FindFieldByFullName(fullName, compilation),
            'P' => FindPropertyByFullName(fullName, compilation),
            'E' => FindEventByFullName(fullName, compilation),
            _ => null
        };
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
        var value = NormalizeHumanQualifiedName(input);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.Contains(':', StringComparison.Ordinal))
        {
            var parts = value.Split(':', 2);
            if (parts.Length == 2)
            {
                var member = ResolveMemberByTypeAndName(parts[0].Trim(), parts[1].Trim(), compilation);
                if (member != null)
                    return member;
            }
        }

        for (var index = value.LastIndexOf('.'); index > 0; index = value.LastIndexOf('.', index - 1))
        {
            if (index >= value.Length - 1)
                continue;

            var member = ResolveMemberByTypeAndName(value[..index].Trim(), value[(index + 1)..].Trim(), compilation);
            if (member != null)
                return member;
        }

        return FindTypeDefinition(value, compilation);
    }

    private static string NormalizeHumanQualifiedName(string input)
    {
        var value = input.Trim();
        if (value.Length > 2 && value[1] == ':' && "TMFPE".Contains(value[0]))
            value = value[2..];

        var parenIndex = value.IndexOf('(');
        if (parenIndex >= 0)
            value = value[..parenIndex];

        return value;
    }

    private IEntity? ResolveMemberByTypeAndName(string typeName, string memberName, ICompilation compilation)
    {
        var type = FindTypeDefinition(typeName, compilation);
        if (type == null)
            return null;

        return (IEntity?)type.Methods.FirstOrDefault(method => string.Equals(method.Name, memberName, StringComparison.OrdinalIgnoreCase))
            ?? (IEntity?)type.Fields.FirstOrDefault(field => string.Equals(field.Name, memberName, StringComparison.OrdinalIgnoreCase))
            ?? (IEntity?)type.Properties.FirstOrDefault(property => string.Equals(property.Name, memberName, StringComparison.OrdinalIgnoreCase))
            ?? type.Events.FirstOrDefault(evt => string.Equals(evt.Name, memberName, StringComparison.OrdinalIgnoreCase));
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

    private IMethod? FindMethodByFullName(string fullName, ICompilation compilation)
    {
        // Parse method full name and find matching method
        // This is simplified - full implementation would handle generics, overloads, etc.
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot < 0) return null;

        var typeName = fullName.Substring(0, lastDot);
        var methodName = fullName.Substring(lastDot + 1);

        // Use cached type lookup if available
        var type = _contextManager.FindTypeByName(typeName) ??
                   compilation.FindType(new ICSharpCode.Decompiler.TypeSystem.FullTypeName(typeName)).GetDefinition();
        return type?.Methods.FirstOrDefault(m => m.Name == methodName);
    }

    private IField? FindFieldByFullName(string fullName, ICompilation compilation)
    {
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot < 0) return null;

        var typeName = fullName.Substring(0, lastDot);
        var fieldName = fullName.Substring(lastDot + 1);

        // Use cached type lookup if available
        var type = _contextManager.FindTypeByName(typeName) ??
                   compilation.FindType(new ICSharpCode.Decompiler.TypeSystem.FullTypeName(typeName)).GetDefinition();
        return type?.Fields.FirstOrDefault(f => f.Name == fieldName);
    }

    private IProperty? FindPropertyByFullName(string fullName, ICompilation compilation)
    {
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot < 0) return null;

        var typeName = fullName.Substring(0, lastDot);
        var propertyName = fullName.Substring(lastDot + 1);

        // Use cached type lookup if available
        var type = _contextManager.FindTypeByName(typeName) ??
                   compilation.FindType(new ICSharpCode.Decompiler.TypeSystem.FullTypeName(typeName)).GetDefinition();
        return type?.Properties.FirstOrDefault(p => p.Name == propertyName);
    }

    private IEvent? FindEventByFullName(string fullName, ICompilation compilation)
    {
        var lastDot = fullName.LastIndexOf('.');
        if (lastDot < 0) return null;

        var typeName = fullName.Substring(0, lastDot);
        var eventName = fullName.Substring(lastDot + 1);

        // Use cached type lookup if available
        var type = _contextManager.FindTypeByName(typeName) ??
                   compilation.FindType(new ICSharpCode.Decompiler.TypeSystem.FullTypeName(typeName)).GetDefinition();
        return type?.Events.FirstOrDefault(e => e.Name == eventName);
    }

    private string FormatMethodSignature(IMethod method)
    {
        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type.Name} {p.Name}"));
        return $"{method.ReturnType.Name} {method.Name}({parameters})";
    }

    private string FormatPropertySignature(IProperty property)
    {
        return $"{property.ReturnType.Name} {property.Name}";
    }

    private string FormatFieldSignature(IField field)
    {
        return $"{field.ReturnType.Name} {field.Name}";
    }

    private string FormatEventSignature(IEvent evt)
    {
        return $"event {evt.ReturnType.Name} {evt.Name}";
    }

    private string FormatTypeSignature(IType type)
    {
        return type.FullName;
    }
}

/// <summary>
/// Resolver cache statistics
/// </summary>
public record ResolverCacheStats(int CachedResolutions, int SuccessfulResolutions, int FailedResolutions);

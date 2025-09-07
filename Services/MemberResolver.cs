using ICSharpCode.Decompiler.TypeSystem;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace DecompilerServer.Services;

/// <summary>
/// Resolves member IDs to IEntity objects and handles member ID normalization.
/// Supports various member ID formats and provides consistent resolution with caching.
/// </summary>
public class MemberResolver
{
    private readonly AssemblyContextManager _contextManager;
    private readonly ConcurrentDictionary<string, IEntity?> _resolutionCache = new();

    public MemberResolver(AssemblyContextManager contextManager)
    {
        _contextManager = contextManager;
    }

    /// <summary>
    /// Resolve a member ID to an IEntity (type, method, field, property, event) with caching
    /// </summary>
    public IEntity? ResolveMember(string memberId)
    {
        if (string.IsNullOrWhiteSpace(memberId))
            return null;

        // Check cache first
        if (_resolutionCache.TryGetValue(memberId, out var cached))
            return cached;

        var compilation = _contextManager.GetCompilation();

        // Try fast path with indexed members first
        var entity = _contextManager.FindMemberById(memberId) ??
                     ResolveMemberByFullName(memberId, compilation) ??
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
        return entity switch
        {
            ITypeDefinition type => $"T:{type.FullName}",
            IMethod method => $"M:{method.FullName}",
            IField field => $"F:{field.FullName}",
            IProperty property => $"P:{property.FullName}",
            IEvent evt => $"E:{evt.FullName}",
            _ => entity.FullName
        };
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

        // Check common patterns: T:, M:, F:, P:, E: prefixes or tokens
        return Regex.IsMatch(memberId, @"^([TMFPE]:.*|0x[0-9A-Fa-f]+|\d+)$");
    }

    /// <summary>
    /// Clear the resolution cache
    /// </summary>
    public void ClearCache()
    {
        _resolutionCache.Clear();
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public ResolverCacheStats GetCacheStats()
    {
        return new ResolverCacheStats
        {
            CachedResolutions = _resolutionCache.Count,
            SuccessfulResolutions = _resolutionCache.Count(kv => kv.Value != null),
            FailedResolutions = _resolutionCache.Count(kv => kv.Value == null)
        };
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
public class ResolverCacheStats
{
    public int CachedResolutions { get; init; }
    public int SuccessfulResolutions { get; init; }
    public int FailedResolutions { get; init; }
}
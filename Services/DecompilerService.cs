using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace DecompilerServer.Services;

/// <summary>
/// Handles C# decompilation with caching and provides source document management.
/// Caches decompiled source with line indexing for efficient slicing.
/// </summary>
public class DecompilerService
{
    private readonly AssemblyContextManager _contextManager;
    private readonly MemberResolver _memberResolver;
    private readonly OriginalSourceResolver _originalSourceResolver;
    private readonly ConcurrentDictionary<string, RawSourceContent> _contentCache = new();
    private readonly ConcurrentDictionary<string, PreparedSourceContent> _preparedSourceCache = new();
    private readonly ConcurrentDictionary<string, string> _memberContentKeyCache = new();
    private readonly object _cacheLock = new();
    private readonly object _decompileLock = new();
    private long _cacheVersion = -1;

    public DecompilerService(AssemblyContextManager contextManager, MemberResolver memberResolver)
    {
        _contextManager = contextManager;
        _memberResolver = memberResolver;
        _originalSourceResolver = new OriginalSourceResolver(contextManager);
    }

    /// <summary>
    /// Decompile a member to C# and return source document metadata
    /// </summary>
    public SourceDocument DecompileMember(string memberId, bool includeHeader = true)
    {
        EnsureCacheCurrent();

        var entity = _memberResolver.ResolveMember(memberId);
        if (entity == null)
            throw new ArgumentException($"Cannot resolve member: {memberId}");

        var contentKey = GetOrCreateContentKey(memberId, entity);
        if (!_contentCache.TryGetValue(contentKey, out var rawContent))
            throw new InvalidOperationException($"Source content was not cached for '{memberId}'");

        var preparedKey = $"{contentKey}:header={includeHeader}";
        var preparedContent = _preparedSourceCache.GetOrAdd(preparedKey, _ => PrepareSourceContent(rawContent, includeHeader));

        return new SourceDocument(
            memberId,
            preparedContent.Language,
            preparedContent.TotalLines,
            preparedContent.Hash,
            preparedContent.Lines,
            includeHeader,
            preparedContent.SourceKind,
            preparedContent.SourcePath,
            preparedContent.SourceUri);
    }

    /// <summary>
    /// Get a slice of source code from a cached document
    /// </summary>
    public SourceSlice GetSourceSlice(string memberId, int startLine = 1, int? endLine = null, bool includeHeader = true)
    {
        var document = DecompileMember(memberId, includeHeader);

        startLine = Math.Max(1, startLine);
        endLine ??= document.TotalLines;
        endLine = Math.Min(document.TotalLines, endLine.Value);

        if (startLine > endLine)
            throw new ArgumentException("Start line cannot be greater than end line");

        var sliceLines = document.Lines
            .Skip(startLine - 1)
            .Take(endLine.Value - startLine + 1)
            .ToArray();

        return new SourceSlice(memberId, document.Language, startLine, endLine.Value, document.TotalLines, document.Hash,
            string.Join('\n', sliceLines), document.SourceKind, document.SourcePath, document.SourceUri);
    }

    /// <summary>
    /// Batch decompile multiple members
    /// </summary>
    public IEnumerable<SourceDocument> BatchDecompile(IEnumerable<string> memberIds, bool includeHeader = true)
    {
        return memberIds.Select(id =>
        {
            try
            {
                return DecompileMember(id, includeHeader);
            }
            catch
            {
                // Return error document for failed decompilation
                return new SourceDocument(id, "C#", 1, "error",
                    new[] { $"// Error decompiling {id}" }, includeHeader);
            }
        });
    }

    /// <summary>
    /// Decompile a specific entity directly rather than its containing type.
    /// This is useful for focused compare workflows where the caller wants a single member body.
    /// </summary>
    public string DecompileEntitySnippet(string memberId, bool includeHeader = false)
    {
        EnsureCacheCurrent();

        var entity = _memberResolver.ResolveMember(memberId);
        if (entity == null)
            throw new ArgumentException($"Cannot resolve member: {memberId}");

        string code;
        lock (_decompileLock)
        {
            var decompiler = _contextManager.GetDecompiler();
            code = NormalizeLineEndings(decompiler.DecompileAsString(new[] { entity.MetadataToken }));
        }

        return includeHeader ? code : StripHeader(code);
    }

    /// <summary>
    /// Clear the source cache
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _contentCache.Clear();
            _preparedSourceCache.Clear();
            _memberContentKeyCache.Clear();
            _originalSourceResolver.ClearCache();
            _cacheVersion = _contextManager.ContextVersion;
        }
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public CacheStats GetCacheStats()
    {
        EnsureCacheCurrent();

        var totalMemoryEstimate =
            _contentCache.Values.Sum(content => content.Code.Length * 2L) +
            _preparedSourceCache.Values.Sum(content => content.Lines.Sum(line => line.Length * 2L));

        return new CacheStats(
            _preparedSourceCache.Count,
            totalMemoryEstimate,
            _contentCache.Count,
            _contentCache.Values.Count(content => content.SourceKind == SourceKinds.Decompiled),
            _contentCache.Values.Count(content => content.SourceKind != SourceKinds.Decompiled),
            _contentCache.Values.Count(content => content.SourceKind == SourceKinds.SourceLink));
    }

    private string GetOrCreateContentKey(string memberId, IEntity entity)
    {
        if (_memberContentKeyCache.TryGetValue(memberId, out var cachedContentKey))
            return cachedContentKey;

        if (entity is ITypeDefinition)
        {
            var originalSource = _originalSourceResolver.TryResolve(entity);
            if (originalSource != null)
            {
                _contentCache.TryAdd(originalSource.CacheKey, new RawSourceContent(
                    originalSource.Language,
                    NormalizeLineEndings(originalSource.Code),
                    originalSource.SourceKind,
                    originalSource.SourcePath,
                    originalSource.SourceUri));
                _memberContentKeyCache[memberId] = originalSource.CacheKey;
                return originalSource.CacheKey;
            }
        }

        var decompiledContentKey = CreateDecompiledContentKey(entity);
        _contentCache.GetOrAdd(decompiledContentKey, _ => new RawSourceContent(
            "C#",
            NormalizeLineEndings(DecompileEntity(entity)),
            SourceKinds.Decompiled,
            null,
            null));
        _memberContentKeyCache[memberId] = decompiledContentKey;
        return decompiledContentKey;
    }

    private PreparedSourceContent PrepareSourceContent(RawSourceContent rawContent, bool includeHeader)
    {
        var transformedCode = includeHeader ? rawContent.Code : StripHeader(rawContent.Code);
        var lines = transformedCode.Split('\n');
        var hash = ComputeHash(transformedCode);

        return new PreparedSourceContent(
            rawContent.Language,
            lines.Length,
            hash,
            lines,
            rawContent.SourceKind,
            rawContent.SourcePath,
            rawContent.SourceUri);
    }

    private string DecompileEntity(IEntity entity)
    {
        lock (_decompileLock)
        {
            var decompiler = _contextManager.GetDecompiler();
            return entity switch
            {
                ITypeDefinition type => DecompileType(type, decompiler),
                IMethod method => DecompileMethod(method, decompiler),
                IField field => DecompileField(field, decompiler),
                IProperty property => DecompileProperty(property, decompiler),
                IEvent evt => DecompileEvent(evt, decompiler),
                _ => throw new NotSupportedException($"Decompilation not supported for entity type: {entity.GetType()}")
            };
        }
    }

    private static string DecompileType(IType type, CSharpDecompiler decompiler)
    {
        return decompiler.DecompileTypeAsString(GetDecompilerTypeName(type));
    }

    private static FullTypeName GetDecompilerTypeName(IType type)
    {
        if (!string.IsNullOrWhiteSpace(type.ReflectionName))
            return new FullTypeName(type.ReflectionName);

        return type switch
        {
            ITypeDefinition typeDefinition => typeDefinition.FullTypeName,
            _ => new FullTypeName(type.FullName)
        };
    }

    private static string StripHeader(string code)
    {
        var lines = code.Split('\n');
        var index = 0;

        while (index < lines.Length && (lines[index].StartsWith("using ") || string.IsNullOrWhiteSpace(lines[index])))
            index++;

        var startIndex = index;
        var endIndex = lines.Length;

        if (index < lines.Length && lines[index].StartsWith("namespace"))
        {
            var nsLine = lines[index];
            var braceStyle = !nsLine.TrimEnd().EndsWith(";");
            index++;

            if (braceStyle && index < lines.Length && lines[index].Trim() == "{")
                index++;

            startIndex = index;

            if (braceStyle)
            {
                endIndex = lines.Length;
                while (endIndex > startIndex && string.IsNullOrWhiteSpace(lines[endIndex - 1]))
                    endIndex--;
                if (endIndex > startIndex && lines[endIndex - 1].Trim() == "}")
                    endIndex--;
            }
        }

        // Create result array with proper size
        var resultLines = new string[endIndex - startIndex];
        for (var i = 0; i < resultLines.Length; i++)
        {
            var line = lines[startIndex + i];
            resultLines[i] = line.StartsWith("    ") ? line[4..] : line;
        }

        return string.Join('\n', resultLines);
    }

    private string DecompileMethod(IMethod method, CSharpDecompiler decompiler)
    {
        return DecompileSingleEntityOrFallback(
            method,
            decompiler,
            () => method.DeclaringType != null
                ? DecompileType(method.DeclaringType, decompiler)
                : $"// Error: Method '{method.Name}' has no declaring type and cannot be decompiled");
    }

    private string DecompileField(IField field, CSharpDecompiler decompiler)
    {
        return DecompileSingleEntityOrFallback(
            field,
            decompiler,
            () => field.DeclaringType != null
                ? DecompileType(field.DeclaringType, decompiler)
                : $"// Error: Field '{field.Name}' has no declaring type and cannot be decompiled");
    }

    private string DecompileProperty(IProperty property, CSharpDecompiler decompiler)
    {
        return DecompileSingleEntityOrFallback(
            property,
            decompiler,
            () => property.DeclaringType != null
                ? DecompileType(property.DeclaringType, decompiler)
                : $"// Error: Property '{property.Name}' has no declaring type and cannot be decompiled");
    }

    private string DecompileEvent(IEvent evt, CSharpDecompiler decompiler)
    {
        return DecompileSingleEntityOrFallback(
            evt,
            decompiler,
            () => evt.DeclaringType != null
                ? DecompileType(evt.DeclaringType, decompiler)
                : $"// Error: Event '{evt.Name}' has no declaring type and cannot be decompiled");
    }

    private string ComputeHash(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private string CreateDecompiledContentKey(IEntity entity)
    {
        return $"decompiled:{_memberResolver.GenerateMemberId(entity)}";
    }

    private static string DecompileSingleEntityOrFallback(
        IEntity entity,
        CSharpDecompiler decompiler,
        Func<string> fallback)
    {
        try
        {
            if (entity.MetadataToken.IsNil)
                return fallback();

            return decompiler.DecompileAsString(new[] { entity.MetadataToken });
        }
        catch
        {
            return fallback();
        }
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

            _contentCache.Clear();
            _preparedSourceCache.Clear();
            _memberContentKeyCache.Clear();
            _originalSourceResolver.ClearCache();
            _cacheVersion = version;
        }
    }

    private sealed record RawSourceContent(
        string Language,
        string Code,
        string SourceKind,
        string? SourcePath,
        string? SourceUri);

    private sealed record PreparedSourceContent(
        string Language,
        int TotalLines,
        string Hash,
        string[] Lines,
        string SourceKind,
        string? SourcePath,
        string? SourceUri);
}

/// <summary>
/// Represents a cached source document
/// </summary>
public record SourceDocument(
    string MemberId,
    string Language,
    int TotalLines,
    string Hash,
    string[] Lines,
    bool IncludeHeader,
    string SourceKind = SourceKinds.Decompiled,
    string? SourcePath = null,
    string? SourceUri = null);

/// <summary>
/// Represents a slice of source code
/// </summary>
public record SourceSlice(
    string MemberId,
    string Language,
    int StartLine,
    int EndLine,
    int TotalLines,
    string Hash,
    string Code,
    string SourceKind = SourceKinds.Decompiled,
    string? SourcePath = null,
    string? SourceUri = null);

/// <summary>
/// Cache statistics
/// </summary>
public record CacheStats(
    int SourceDocuments,
    long TotalMemoryEstimate,
    int RawContents = 0,
    int DecompiledContents = 0,
    int OriginalSourceContents = 0,
    int SourceLinkContents = 0);

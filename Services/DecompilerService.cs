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
    private readonly ConcurrentDictionary<string, SourceDocument> _sourceCache = new();

    public DecompilerService(AssemblyContextManager contextManager, MemberResolver memberResolver)
    {
        _contextManager = contextManager;
        _memberResolver = memberResolver;
    }

    /// <summary>
    /// Decompile a member to C# and return source document metadata
    /// </summary>
    public SourceDocument DecompileMember(string memberId, bool includeHeader = true)
    {
        // Check cache first
        var cacheKey = $"{memberId}:{includeHeader}";
        if (_sourceCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var entity = _memberResolver.ResolveMember(memberId);
        if (entity == null)
            throw new ArgumentException($"Cannot resolve member: {memberId}");

        var decompiler = _contextManager.GetDecompiler();
        var code = DecompileEntity(entity, decompiler, includeHeader);

        var lines = code.Split('\n');
        var hash = ComputeHash(code);

        var document = new SourceDocument
        {
            MemberId = memberId,
            Language = "C#",
            TotalLines = lines.Length,
            Hash = hash,
            Lines = lines,
            IncludeHeader = includeHeader
        };

        _sourceCache[cacheKey] = document;
        return document;
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

        return new SourceSlice
        {
            MemberId = memberId,
            Language = document.Language,
            StartLine = startLine,
            EndLine = endLine.Value,
            TotalLines = document.TotalLines,
            Hash = document.Hash,
            Code = string.Join('\n', sliceLines)
        };
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
                return new SourceDocument
                {
                    MemberId = id,
                    Language = "C#",
                    TotalLines = 1,
                    Hash = "error",
                    Lines = new[] { $"// Error decompiling {id}" },
                    IncludeHeader = includeHeader
                };
            }
        });
    }

    /// <summary>
    /// Clear the source cache
    /// </summary>
    public void ClearCache()
    {
        _sourceCache.Clear();
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public CacheStats GetCacheStats()
    {
        return new CacheStats
        {
            SourceDocuments = _sourceCache.Count,
            TotalMemoryEstimate = _sourceCache.Values.Sum(d => d.Lines.Sum(l => l.Length * 2)) // rough estimate
        };
    }

    private string DecompileEntity(IEntity entity, CSharpDecompiler decompiler, bool includeHeader)
    {
        return entity switch
        {
            ITypeDefinition type => decompiler.DecompileTypeAsString(type.FullTypeName),
            IMethod method => DecompileMethod(method, decompiler),
            IField field => DecompileField(field, decompiler),
            IProperty property => DecompileProperty(property, decompiler),
            IEvent evt => DecompileEvent(evt, decompiler),
            _ => throw new NotSupportedException($"Decompilation not supported for entity type: {entity.GetType()}")
        };
    }

    private string DecompileMethod(IMethod method, CSharpDecompiler decompiler)
    {
        // For members, we decompile the containing type and extract the member
        return decompiler.DecompileTypeAsString(new ICSharpCode.Decompiler.TypeSystem.FullTypeName(method.DeclaringType!.FullName));
    }

    private string DecompileField(IField field, CSharpDecompiler decompiler)
    {
        return decompiler.DecompileTypeAsString(new ICSharpCode.Decompiler.TypeSystem.FullTypeName(field.DeclaringType!.FullName));
    }

    private string DecompileProperty(IProperty property, CSharpDecompiler decompiler)
    {
        return decompiler.DecompileTypeAsString(new ICSharpCode.Decompiler.TypeSystem.FullTypeName(property.DeclaringType!.FullName));
    }

    private string DecompileEvent(IEvent evt, CSharpDecompiler decompiler)
    {
        return decompiler.DecompileTypeAsString(new ICSharpCode.Decompiler.TypeSystem.FullTypeName(evt.DeclaringType!.FullName));
    }

    private string ComputeHash(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Represents a cached source document
/// </summary>
public class SourceDocument
{
    public required string MemberId { get; init; }
    public required string Language { get; init; }
    public required int TotalLines { get; init; }
    public required string Hash { get; init; }
    public required string[] Lines { get; init; }
    public required bool IncludeHeader { get; init; }
}

/// <summary>
/// Represents a slice of source code
/// </summary>
public class SourceSlice
{
    public required string MemberId { get; init; }
    public required string Language { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required int TotalLines { get; init; }
    public required string Hash { get; init; }
    public required string Code { get; init; }
}

/// <summary>
/// Cache statistics
/// </summary>
public class CacheStats
{
    public int SourceDocuments { get; init; }
    public long TotalMemoryEstimate { get; init; }
}
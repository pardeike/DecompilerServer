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

        var document = new SourceDocument(memberId, "C#", lines.Length, hash, lines, includeHeader);

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

        return new SourceSlice(memberId, document.Language, startLine, endLine.Value, document.TotalLines, document.Hash,
            string.Join('\n', sliceLines));
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
        return new CacheStats(_sourceCache.Count,
            _sourceCache.Values.Sum(d => d.Lines.Sum(l => l.Length * 2))); // rough estimate
    }

    private string DecompileEntity(IEntity entity, CSharpDecompiler decompiler, bool includeHeader)
    {
        var code = entity switch
        {
            ITypeDefinition type => decompiler.DecompileTypeAsString(type.FullTypeName),
            IMethod method => DecompileMethod(method, decompiler),
            IField field => DecompileField(field, decompiler),
            IProperty property => DecompileProperty(property, decompiler),
            IEvent evt => DecompileEvent(evt, decompiler),
            _ => throw new NotSupportedException($"Decompilation not supported for entity type: {entity.GetType()}")
        };

        return includeHeader ? code : StripHeader(code);
    }

    private static string StripHeader(string code)
    {
        var lines = code.Split('\n').ToList();
        var index = 0;

        while (index < lines.Count && (lines[index].StartsWith("using ") || string.IsNullOrWhiteSpace(lines[index])))
            index++;

        if (index < lines.Count && lines[index].StartsWith("namespace"))
        {
            var nsLine = lines[index];
            var braceStyle = !nsLine.TrimEnd().EndsWith(";");
            index++;

            if (braceStyle && index < lines.Count && lines[index].Trim() == "{")
                index++;

            var end = lines.Count;
            if (braceStyle)
            {
                while (end > index && string.IsNullOrWhiteSpace(lines[end - 1]))
                    end--;
                if (end > index && lines[end - 1].Trim() == "}")
                    end--;
            }

            lines = lines.GetRange(index, end - index);
        }
        else
        {
            lines = lines.GetRange(index, lines.Count - index);
        }

        for (var i = 0; i < lines.Count; i++)
            if (lines[i].StartsWith("    "))
                lines[i] = lines[i][4..];

        return string.Join('\n', lines);
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
public record SourceDocument(string MemberId, string Language, int TotalLines, string Hash, string[] Lines, bool IncludeHeader);

/// <summary>
/// Represents a slice of source code
/// </summary>
public record SourceSlice(string MemberId, string Language, int StartLine, int EndLine, int TotalLines, string Hash, string Code);

/// <summary>
/// Cache statistics
/// </summary>
public record CacheStats(int SourceDocuments, long TotalMemoryEstimate);
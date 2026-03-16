using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Metadata;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DecompilerServer.Services;

/// <summary>
/// Resolves original source files from portable PDB data when available.
/// Prefers embedded sources, then verified local files, then verified SourceLink downloads.
/// </summary>
public sealed class OriginalSourceResolver : IDisposable
{
    private static readonly Guid EmbeddedSourceGuid = new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");
    private static readonly Guid SourceLinkGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    private static readonly Guid Md5Guid = new("406EA660-64CF-4C82-B6F0-42D48172A799");
    private static readonly Guid Sha1Guid = new("FF1816EC-AA5E-4D10-87F7-6F4963833460");
    private static readonly Guid Sha256Guid = new("8829D00F-11B8-4213-878B-770E8597AC16");

    private static readonly HttpClient SourceLinkHttpClient = CreateHttpClient();

    private readonly AssemblyContextManager _contextManager;
    private readonly ConcurrentDictionary<string, CachedDocumentLoad> _documentCache = new();
    private readonly object _cacheLock = new();

    private long _cacheVersion = -1;
    private PortablePdbContext? _pdbContext;

    public OriginalSourceResolver(AssemblyContextManager contextManager)
    {
        _contextManager = contextManager;
    }

    public ResolvedOriginalSource? TryResolve(IEntity entity)
    {
        EnsureCacheCurrent();

        var pdbContext = GetOrCreatePortablePdbContext();
        if (pdbContext == null)
            return null;

        var documents = GetCandidateDocuments(entity, pdbContext.Reader);
        if (documents.Count != 1)
            return null;

        var documentRef = documents[0];
        var cachedLoad = _documentCache.GetOrAdd(documentRef.Name, _ => LoadDocument(documentRef, pdbContext));
        return cachedLoad.Source;
    }

    public OriginalSourceCacheStats GetCacheStats()
    {
        EnsureCacheCurrent();

        return new OriginalSourceCacheStats(
            _documentCache.Count,
            _documentCache.Values.Count(entry => entry.Source != null && entry.Source.SourceKind == SourceKinds.EmbeddedSource),
            _documentCache.Values.Count(entry => entry.Source != null && entry.Source.SourceKind == SourceKinds.LocalSource),
            _documentCache.Values.Count(entry => entry.Source != null && entry.Source.SourceKind == SourceKinds.SourceLink),
            _documentCache.Values.Sum(entry => entry.EstimatedBytes));
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _documentCache.Clear();
            DisposePdbContext();
            _cacheVersion = _contextManager.ContextVersion;
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

            _documentCache.Clear();
            DisposePdbContext();
            _cacheVersion = version;
        }
    }

    private PortablePdbContext? GetOrCreatePortablePdbContext()
    {
        if (_pdbContext != null)
            return _pdbContext;

        lock (_cacheLock)
        {
            if (_pdbContext != null)
                return _pdbContext;

            if (!_contextManager.IsLoaded)
                return null;

            if (!TryOpenPortablePdb(_contextManager.GetPEFile(), out var provider, out var pdbFileName))
                return null;

            var reader = provider.GetMetadataReader();
            _pdbContext = new PortablePdbContext(provider, reader, pdbFileName, ParseSourceLinkMap(reader));
            return _pdbContext;
        }
    }

    private static bool TryOpenPortablePdb(PEFile module, out MetadataReaderProvider provider, out string? pdbFileName)
    {
        provider = null!;
        pdbFileName = null;

        foreach (var entry in module.Reader.ReadDebugDirectory())
        {
            if (entry.IsPortableCodeView &&
                module.Reader.TryOpenAssociatedPortablePdb(module.FileName, OpenMemoryMappedStream, out var associatedProvider, out var associatedPdbFileName))
            {
                provider = associatedProvider!;
                pdbFileName = associatedPdbFileName;
                return true;
            }

            if (entry.Type != DebugDirectoryEntryType.CodeView)
                continue;

            var localPdbPath = Path.Combine(
                Path.GetDirectoryName(module.FileName) ?? string.Empty,
                Path.GetFileNameWithoutExtension(module.FileName) + ".pdb");

            var stream = OpenMemoryMappedStream(localPdbPath);
            if (stream == null)
                continue;

            if (LooksLikeLegacyPdb(stream))
            {
                stream.Dispose();
                continue;
            }

            stream.Position = 0;
            provider = MetadataReaderProvider.FromPortablePdbStream(stream);
            pdbFileName = localPdbPath;
            return true;
        }

        return false;
    }

    private static Stream? OpenMemoryMappedStream(string fileName)
    {
        if (!File.Exists(fileName))
            return null;

        var memory = new MemoryStream();
        using var stream = File.OpenRead(fileName);
        stream.CopyTo(memory);
        memory.Position = 0;
        return memory;
    }

    private static bool LooksLikeLegacyPdb(Stream stream)
    {
        const string legacyPdbPrefix = "Microsoft C/C++ MSF 7.00";
        var buffer = new byte[legacyPdbPrefix.Length];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);
        return bytesRead == legacyPdbPrefix.Length &&
               Encoding.ASCII.GetString(buffer) == legacyPdbPrefix;
    }

    private static SourceLinkMap? ParseSourceLinkMap(MetadataReader reader)
    {
        foreach (var handle in reader.CustomDebugInformation)
        {
            var debugInfo = reader.GetCustomDebugInformation(handle);
            if (debugInfo.Kind.IsNil || debugInfo.Value.IsNil)
                continue;

            var parentKind = debugInfo.Parent.Kind;
            if (parentKind != HandleKind.ModuleDefinition && parentKind != HandleKind.AssemblyDefinition)
                continue;

            if (reader.GetGuid(debugInfo.Kind) != SourceLinkGuid)
                continue;

            var blobReader = reader.GetBlobReader(debugInfo.Value);
            var json = blobReader.ReadUTF8(blobReader.RemainingBytes);
            return SourceLinkMap.Parse(json);
        }

        return null;
    }

    private static List<DocumentReference> GetCandidateDocuments(IEntity entity, MetadataReader reader)
    {
        return entity switch
        {
            IMethod method => CollectMethodDocuments(new[] { method }, reader),
            IProperty property => CollectMethodDocuments(new[] { property.Getter, property.Setter }.Where(m => m != null)!, reader),
            IEvent evt => CollectMethodDocuments(new[] { evt.AddAccessor, evt.RemoveAccessor, evt.InvokeAccessor }.Where(m => m != null)!, reader),
            IField field when field.DeclaringType != null => CollectTypeDocuments(field.DeclaringType.GetDefinition(), reader),
            ITypeDefinition typeDefinition => CollectTypeDocuments(typeDefinition, reader),
            _ => new List<DocumentReference>()
        };
    }

    private static List<DocumentReference> CollectTypeDocuments(ITypeDefinition? typeDefinition, MetadataReader reader)
    {
        if (typeDefinition == null)
            return new List<DocumentReference>();

        return CollectMethodDocuments(typeDefinition.Methods, reader);
    }

    private static List<DocumentReference> CollectMethodDocuments(IEnumerable<IMethod> methods, MetadataReader reader)
    {
        var documents = new Dictionary<string, DocumentReference>(StringComparer.OrdinalIgnoreCase);

        foreach (var method in methods)
        {
            if (method.MetadataToken.IsNil || method.MetadataToken.Kind != HandleKind.MethodDefinition)
                continue;

            var handle = (MethodDefinitionHandle)method.MetadataToken;
            var methodDebugInformation = reader.GetMethodDebugInformation(handle);

            AddDocumentReference(methodDebugInformation.Document, reader, documents);

            foreach (var sequencePoint in methodDebugInformation.GetSequencePoints())
            {
                AddDocumentReference(sequencePoint.Document, reader, documents);
            }
        }

        return documents.Values.ToList();
    }

    private static void AddDocumentReference(DocumentHandle handle, MetadataReader reader, IDictionary<string, DocumentReference> documents)
    {
        if (handle.IsNil)
            return;

        var document = reader.GetDocument(handle);
        var name = reader.GetString(document.Name);
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (!documents.ContainsKey(name))
        {
            documents[name] = new DocumentReference(handle, name);
        }
    }

    private CachedDocumentLoad LoadDocument(DocumentReference documentRef, PortablePdbContext pdbContext)
    {
        var document = pdbContext.Reader.GetDocument(documentRef.Handle);
        var language = DetectLanguage(documentRef.Name);

        if (TryGetEmbeddedSource(documentRef.Handle, pdbContext.Reader, out var embeddedBytes))
        {
            return CachedDocumentLoad.Success(new ResolvedOriginalSource(
                $"original:{SourceKinds.EmbeddedSource}:{documentRef.Name}",
                language,
                DecodeSourceText(embeddedBytes),
                SourceKinds.EmbeddedSource,
                documentRef.Name,
                null));
        }

        if (TryReadVerifiedLocalFile(documentRef.Name, document, pdbContext.Reader, out var localBytes))
        {
            return CachedDocumentLoad.Success(new ResolvedOriginalSource(
                $"original:{SourceKinds.LocalSource}:{documentRef.Name}",
                language,
                DecodeSourceText(localBytes),
                SourceKinds.LocalSource,
                documentRef.Name,
                null));
        }

        if (TryReadVerifiedSourceLinkFile(documentRef.Name, document, pdbContext, out var sourceLinkBytes, out var sourceUri))
        {
            return CachedDocumentLoad.Success(new ResolvedOriginalSource(
                $"original:{SourceKinds.SourceLink}:{sourceUri}",
                language,
                DecodeSourceText(sourceLinkBytes),
                SourceKinds.SourceLink,
                documentRef.Name,
                sourceUri));
        }

        return CachedDocumentLoad.Miss();
    }

    private static bool TryGetEmbeddedSource(DocumentHandle documentHandle, MetadataReader reader, out byte[] sourceBytes)
    {
        foreach (var debugHandle in reader.GetCustomDebugInformation(documentHandle))
        {
            var debugInfo = reader.GetCustomDebugInformation(debugHandle);
            if (debugInfo.Kind.IsNil || debugInfo.Value.IsNil)
                continue;

            if (reader.GetGuid(debugInfo.Kind) != EmbeddedSourceGuid)
                continue;

            var blobReader = reader.GetBlobReader(debugInfo.Value);
            var format = blobReader.ReadInt32();

            if (format == 0)
            {
                sourceBytes = blobReader.ReadBytes(blobReader.RemainingBytes);
                return true;
            }

            if (format > 0)
            {
                using var compressedStream = new MemoryStream(blobReader.ReadBytes(blobReader.RemainingBytes));
                using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
                using var output = new MemoryStream(format);
                deflateStream.CopyTo(output);
                sourceBytes = output.ToArray();
                return sourceBytes.Length == format;
            }
        }

        sourceBytes = Array.Empty<byte>();
        return false;
    }

    private static bool TryReadVerifiedLocalFile(string documentName, Document document, MetadataReader reader, out byte[] sourceBytes)
    {
        sourceBytes = Array.Empty<byte>();
        if (!File.Exists(documentName))
            return false;

        var bytes = File.ReadAllBytes(documentName);
        if (!VerifyDocumentHash(document, reader, bytes))
            return false;

        sourceBytes = bytes;
        return true;
    }

    private static bool TryReadVerifiedSourceLinkFile(
        string documentName,
        Document document,
        PortablePdbContext pdbContext,
        out byte[] sourceBytes,
        out string? sourceUri)
    {
        sourceBytes = Array.Empty<byte>();
        sourceUri = null;

        if (pdbContext.SourceLinkMap == null || !pdbContext.SourceLinkMap.TryResolve(documentName, out var resolvedUri))
            return false;

        try
        {
            using var response = SourceLinkHttpClient.GetAsync(resolvedUri).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return false;

            var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if (!VerifyDocumentHash(document, pdbContext.Reader, bytes))
                return false;

            sourceBytes = bytes;
            sourceUri = resolvedUri;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyDocumentHash(Document document, MetadataReader reader, byte[] sourceBytes)
    {
        if (document.Hash.IsNil || document.HashAlgorithm.IsNil)
            return true;

        var expectedHash = reader.GetBlobBytes(document.Hash);
        if (expectedHash.Length == 0)
            return true;

        var algorithm = reader.GetGuid(document.HashAlgorithm);
        byte[]? actualHash = algorithm switch
        {
            var value when value == Md5Guid => MD5.HashData(sourceBytes),
            var value when value == Sha1Guid => SHA1.HashData(sourceBytes),
            var value when value == Sha256Guid => SHA256.HashData(sourceBytes),
            _ => null
        };

        return actualHash == null || actualHash.AsSpan().SequenceEqual(expectedHash);
    }

    private static string DecodeSourceText(byte[] sourceBytes)
    {
        using var stream = new MemoryStream(sourceBytes);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string DetectLanguage(string documentName)
    {
        var extension = Path.GetExtension(documentName);
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "C#",
            ".vb" => "VB",
            _ => "C#"
        };
    }

    private static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private void DisposePdbContext()
    {
        _pdbContext?.Dispose();
        _pdbContext = null;
    }

    public void Dispose()
    {
        lock (_cacheLock)
        {
            _documentCache.Clear();
            DisposePdbContext();
        }
    }

    private sealed record PortablePdbContext(
        MetadataReaderProvider Provider,
        MetadataReader Reader,
        string? PdbFileName,
        SourceLinkMap? SourceLinkMap) : IDisposable
    {
        public void Dispose()
        {
            Provider.Dispose();
        }
    }

    private sealed record DocumentReference(DocumentHandle Handle, string Name);

    private sealed record CachedDocumentLoad(ResolvedOriginalSource? Source, long EstimatedBytes)
    {
        public static CachedDocumentLoad Success(ResolvedOriginalSource source)
        {
            var estimatedBytes = source.Code.Length * 2L;
            estimatedBytes += (source.SourcePath?.Length ?? 0) * 2L;
            estimatedBytes += (source.SourceUri?.Length ?? 0) * 2L;
            return new CachedDocumentLoad(source, estimatedBytes);
        }

        public static CachedDocumentLoad Miss() => new(null, 0);
    }
}

public sealed record ResolvedOriginalSource(
    string CacheKey,
    string Language,
    string Code,
    string SourceKind,
    string? SourcePath,
    string? SourceUri);

public static class SourceKinds
{
    public const string Decompiled = "decompiled";
    public const string EmbeddedSource = "embeddedSource";
    public const string LocalSource = "localSource";
    public const string SourceLink = "sourceLink";
}

public sealed record OriginalSourceCacheStats(
    int CachedDocuments,
    int EmbeddedSources,
    int LocalSources,
    int SourceLinkSources,
    long TotalMemoryEstimate);

internal sealed class SourceLinkMap
{
    private readonly IReadOnlyList<SourceLinkEntry> _entries;

    private SourceLinkMap(IReadOnlyList<SourceLinkEntry> entries)
    {
        _entries = entries;
    }

    public static SourceLinkMap? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("documents", out var documents) || documents.ValueKind != JsonValueKind.Object)
            return null;

        var entries = new List<SourceLinkEntry>();
        foreach (var property in documents.EnumerateObject())
        {
            entries.Add(SourceLinkEntry.Create(property.Name, property.Value.GetString() ?? string.Empty));
        }

        return entries.Count > 0 ? new SourceLinkMap(entries) : null;
    }

    public bool TryResolve(string documentName, out string? resolvedUri)
    {
        var normalizedName = NormalizePath(documentName);

        foreach (var entry in _entries)
        {
            if (entry.TryResolve(normalizedName, out resolvedUri))
                return true;
        }

        resolvedUri = null;
        return false;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private sealed record SourceLinkEntry(
        string PatternPrefix,
        string PatternSuffix,
        string UrlPrefix,
        string UrlSuffix,
        bool HasWildcard)
    {
        public static SourceLinkEntry Create(string documentPattern, string urlPattern)
        {
            var normalizedDocumentPattern = NormalizePath(documentPattern);
            var normalizedUrlPattern = urlPattern.Replace('\\', '/');

            var documentWildcardIndex = normalizedDocumentPattern.IndexOf('*');
            var urlWildcardIndex = normalizedUrlPattern.IndexOf('*');

            if (documentWildcardIndex < 0 || urlWildcardIndex < 0)
            {
                return new SourceLinkEntry(
                    normalizedDocumentPattern,
                    string.Empty,
                    normalizedUrlPattern,
                    string.Empty,
                    false);
            }

            return new SourceLinkEntry(
                normalizedDocumentPattern[..documentWildcardIndex],
                normalizedDocumentPattern[(documentWildcardIndex + 1)..],
                normalizedUrlPattern[..urlWildcardIndex],
                normalizedUrlPattern[(urlWildcardIndex + 1)..],
                true);
        }

        public bool TryResolve(string normalizedDocumentName, out string? resolvedUri)
        {
            if (!HasWildcard)
            {
                if (string.Equals(normalizedDocumentName, PatternPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedUri = UrlPrefix;
                    return true;
                }

                resolvedUri = null;
                return false;
            }

            if (!normalizedDocumentName.StartsWith(PatternPrefix, StringComparison.OrdinalIgnoreCase) ||
                !normalizedDocumentName.EndsWith(PatternSuffix, StringComparison.OrdinalIgnoreCase))
            {
                resolvedUri = null;
                return false;
            }

            var wildcardLength = normalizedDocumentName.Length - PatternPrefix.Length - PatternSuffix.Length;
            if (wildcardLength < 0)
            {
                resolvedUri = null;
                return false;
            }

            var wildcardValue = normalizedDocumentName.Substring(PatternPrefix.Length, wildcardLength);
            var escapedValue = EscapePathSegments(wildcardValue);
            resolvedUri = $"{UrlPrefix}{escapedValue}{UrlSuffix}";
            return true;
        }

        private static string NormalizePath(string path) => path.Replace('\\', '/');

        private static string EscapePathSegments(string path)
        {
            var normalized = NormalizePath(path);
            var segments = normalized.Split('/', StringSplitOptions.None)
                .Select(Uri.EscapeDataString);
            return string.Join("/", segments);
        }
    }
}

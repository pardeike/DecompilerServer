using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Collections.Concurrent;

namespace DecompilerServer.Services;

/// <summary>
/// Manages the loaded assembly context, including PEFile, TypeSystem, and CSharpDecompiler instances.
/// Handles assembly loading, resolver configuration, and lazy indexing with enhanced caching.
/// </summary>
public class AssemblyContextManager : IDisposable
{
    private PEFile? _peFile;
    private ICompilation? _compilation;
    private CSharpDecompiler? _decompiler;
    private UniversalAssemblyResolver? _resolver;
    private bool _disposed;
    private readonly ReaderWriterLockSlim _lock = new();

    // Lazy indexes - built on-demand and cached
    private Lazy<ConcurrentDictionary<string, ITypeDefinition>> _typeByNameIndex = null!;
    private Lazy<ConcurrentDictionary<string, List<ITypeDefinition>>> _typesByNamespaceIndex = null!;
    private Lazy<ConcurrentDictionary<string, IEntity>> _memberByIdIndex = null!;
    private Lazy<HashSet<string>> _namespacesIndex = null!;

    public AssemblyContextManager()
    {
        InitializeLazyIndexes();
    }

    private void InitializeLazyIndexes()
    {
        _typeByNameIndex = new Lazy<ConcurrentDictionary<string, ITypeDefinition>>(BuildTypeByNameIndex);
        _typesByNamespaceIndex = new Lazy<ConcurrentDictionary<string, List<ITypeDefinition>>>(BuildTypesByNamespaceIndex);
        _memberByIdIndex = new Lazy<ConcurrentDictionary<string, IEntity>>(BuildMemberByIdIndex);
        _namespacesIndex = new Lazy<HashSet<string>>(BuildNamespacesIndex);
    }

    public bool IsLoaded => _peFile != null && _compilation != null && _decompiler != null;
    public string? AssemblyPath { get; private set; }
    public string? Mvid { get; private set; }
    public DateTime? LoadedAtUtc { get; private set; }

    // Basic statistics with enhanced caching awareness
    public int TypeCount => _compilation?.MainModule.TypeDefinitions.Count() ?? 0;
    public int NamespaceCount => IsLoaded ? _namespacesIndex.Value.Count : 0;
    public bool TypeIndexReady => _typeByNameIndex.IsValueCreated;
    public bool NamespaceIndexReady => _typesByNamespaceIndex.IsValueCreated;
    public bool MemberIndexReady => _memberByIdIndex.IsValueCreated;

    /// <summary>
    /// Load an assembly and initialize the decompilation context with auto-detection support
    /// </summary>
    public void LoadAssembly(string gameDir, string assemblyFile = "Assembly-CSharp.dll", string[]? additionalSearchDirs = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AssemblyContextManager));

        _lock.EnterWriteLock();
        try
        {
            // Dispose existing context and reset lazy indexes
            DisposeContext();

            // Auto-detect Assembly-CSharp.dll if gameDir provided without specific file
            var assemblyPath = ResolveAssemblyPath(gameDir, assemblyFile);

            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException($"Assembly not found: {assemblyPath}");

            // Create resolver and add search directories
            _resolver = new UniversalAssemblyResolver(assemblyPath, false, null);

            // Add standard Unity search paths
            AddUnitySearchPaths(_resolver, gameDir);

            // Add additional search directories
            if (additionalSearchDirs != null)
            {
                foreach (var dir in additionalSearchDirs)
                {
                    if (Directory.Exists(dir))
                        _resolver.AddSearchDirectory(dir);
                }
            }

            // Load PEFile
            _peFile = new PEFile(assemblyPath);

            // Create TypeSystem
            _compilation = new DecompilerTypeSystem(_peFile, _resolver);

            // Create decompiler with enhanced settings
            var settings = new DecompilerSettings
            {
                UsingDeclarations = true,
                ShowXmlDocumentation = true,
                NamedArguments = true
            };
            _decompiler = new CSharpDecompiler(_peFile, _resolver, settings);

            // Extract MVID
            Mvid = _peFile.Metadata.GetModuleDefinition().Mvid.ToString();

            AssemblyPath = assemblyPath;
            LoadedAtUtc = DateTime.UtcNow;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Get the current decompiler instance
    /// </summary>
    public CSharpDecompiler GetDecompiler()
    {
        _lock.EnterReadLock();
        try
        {
            EnsureLoaded();
            return _decompiler!;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Get the current compilation/type system
    /// </summary>
    public ICompilation GetCompilation()
    {
        _lock.EnterReadLock();
        try
        {
            EnsureLoaded();
            return _compilation!;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Get the current PEFile
    /// </summary>
    public PEFile GetPEFile()
    {
        _lock.EnterReadLock();
        try
        {
            EnsureLoaded();
            return _peFile!;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Get all namespaces in the assembly (cached)
    /// </summary>
    public IEnumerable<string> GetNamespaces()
    {
        if (!IsLoaded) return Enumerable.Empty<string>();
        return _namespacesIndex.Value;
    }

    /// <summary>
    /// Get all types in the assembly
    /// </summary>
    public IEnumerable<ITypeDefinition> GetAllTypes()
    {
        if (!IsLoaded) return Enumerable.Empty<ITypeDefinition>();
        return _compilation!.MainModule.TypeDefinitions;
    }

    /// <summary>
    /// Get types in a specific namespace (cached)
    /// </summary>
    public IEnumerable<ITypeDefinition> GetTypesInNamespace(string namespaceName)
    {
        if (!IsLoaded) return Enumerable.Empty<ITypeDefinition>();
        return _typesByNamespaceIndex.Value.TryGetValue(namespaceName, out var types) ? types : Enumerable.Empty<ITypeDefinition>();
    }

    /// <summary>
    /// Find type by name (cached)
    /// </summary>
    public ITypeDefinition? FindTypeByName(string typeName)
    {
        if (!IsLoaded) return null;
        return _typeByNameIndex.Value.TryGetValue(typeName, out var type) ? type : null;
    }

    /// <summary>
    /// Find member by ID (cached)
    /// </summary>
    public IEntity? FindMemberById(string memberId)
    {
        if (!IsLoaded) return null;
        return _memberByIdIndex.Value.TryGetValue(memberId, out var member) ? member : null;
    }

    /// <summary>
    /// Warm up indexes for better performance
    /// </summary>
    public void WarmIndexes()
    {
        if (!IsLoaded) return;
        
        // Force creation of all lazy indexes
        _ = _typeByNameIndex.Value;
        _ = _typesByNamespaceIndex.Value;
        _ = _memberByIdIndex.Value;
        _ = _namespacesIndex.Value;
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public IndexStats GetIndexStats()
    {
        return new IndexStats
        {
            TypeIndexReady = TypeIndexReady,
            NamespaceIndexReady = NamespaceIndexReady,
            MemberIndexReady = MemberIndexReady,
            TypeCount = TypeCount,
            NamespaceCount = NamespaceCount,
            IndexedMemberCount = MemberIndexReady ? _memberByIdIndex.Value.Count : 0
        };
    }

    /// <summary>
    /// Update decompiler settings
    /// </summary>
    public void UpdateSettings(DecompilerSettings settings)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!IsLoaded) return;
            _decompiler = new CSharpDecompiler(_peFile!, _resolver!, settings);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private string ResolveAssemblyPath(string gameDir, string assemblyFile)
    {
        // If absolute path provided, use it
        if (Path.IsPathRooted(assemblyFile))
            return assemblyFile;

        // Try direct path in gameDir
        var assemblyPath = Path.Combine(gameDir, assemblyFile);
        if (File.Exists(assemblyPath))
            return assemblyPath;

        // Try common Unity paths for Assembly-CSharp.dll
        var unityPaths = new[]
        {
            Path.Combine(gameDir, "Managed", assemblyFile),
            Path.Combine(gameDir, "Data", "Managed", assemblyFile),
            Path.Combine(gameDir, $"{Path.GetFileNameWithoutExtension(gameDir)}_Data", "Managed", assemblyFile),
            // Unity standalone builds
            Path.Combine(gameDir, "Contents", "Resources", "Data", "Managed", assemblyFile),
            // UWP builds
            Path.Combine(gameDir, "Package", "Managed", assemblyFile)
        };

        foreach (var path in unityPaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Return original path if not found (will throw FileNotFoundException later)
        return assemblyPath;
    }

    private ConcurrentDictionary<string, ITypeDefinition> BuildTypeByNameIndex()
    {
        var index = new ConcurrentDictionary<string, ITypeDefinition>();
        if (!IsLoaded) return index;

        foreach (var type in _compilation!.MainModule.TypeDefinitions)
        {
            index.TryAdd(type.FullName, type);
            index.TryAdd(type.Name, type); // Also index by simple name
        }
        return index;
    }

    private ConcurrentDictionary<string, List<ITypeDefinition>> BuildTypesByNamespaceIndex()
    {
        var index = new ConcurrentDictionary<string, List<ITypeDefinition>>();
        if (!IsLoaded) return index;

        foreach (var type in _compilation!.MainModule.TypeDefinitions)
        {
            var ns = type.Namespace ?? "";
            index.AddOrUpdate(ns, new List<ITypeDefinition> { type }, (_, list) => { list.Add(type); return list; });
        }
        return index;
    }

    private ConcurrentDictionary<string, IEntity> BuildMemberByIdIndex()
    {
        var index = new ConcurrentDictionary<string, IEntity>();
        if (!IsLoaded) return index;

        foreach (var type in _compilation!.MainModule.TypeDefinitions)
        {
            // Index type itself
            index.TryAdd($"T:{type.FullName}", type);

            // Index members
            foreach (var method in type.Methods)
            {
                index.TryAdd($"M:{method.FullName}", method);
            }
            foreach (var field in type.Fields)
            {
                index.TryAdd($"F:{field.FullName}", field);
            }
            foreach (var property in type.Properties)
            {
                index.TryAdd($"P:{property.FullName}", property);
            }
            foreach (var evt in type.Events)
            {
                index.TryAdd($"E:{evt.FullName}", evt);
            }
        }
        return index;
    }

    private HashSet<string> BuildNamespacesIndex()
    {
        if (!IsLoaded) return new HashSet<string>();

        return _compilation!.MainModule.TypeDefinitions
            .Select(t => t.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .ToHashSet()!;
    }

    private void AddUnitySearchPaths(UniversalAssemblyResolver resolver, string gameDir)
    {
        // Common Unity assembly paths
        var searchPaths = new[]
        {
            Path.Combine(gameDir, "Managed"),
            Path.Combine(gameDir, "MonoBleedingEdge", "lib", "mono", "4.0"),
            Path.Combine(gameDir, "Data", "Managed"),
            Path.Combine(gameDir, "Data", "MonoBleedingEdge", "lib", "mono", "4.0")
        };

        foreach (var path in searchPaths)
        {
            if (Directory.Exists(path))
                resolver.AddSearchDirectory(path);
        }
    }

    private void EnsureLoaded()
    {
        if (!IsLoaded)
            throw new InvalidOperationException("No assembly is loaded. Call LoadAssembly first.");
    }

    private void DisposeContext()
    {
        _decompiler = null;
        _compilation = null;
        _peFile?.Dispose();
        _peFile = null;
        _resolver = null;
        AssemblyPath = null;
        Mvid = null;
        LoadedAtUtc = null;

        // Recreate lazy indexes to reset them
        InitializeLazyIndexes();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.EnterWriteLock();
            try
            {
                DisposeContext();
                _disposed = true;
            }
            finally
            {
                _lock.ExitWriteLock();
                _lock.Dispose();
            }
        }
    }
}

/// <summary>
/// Index statistics
/// </summary>
public class IndexStats
{
    public bool TypeIndexReady { get; init; }
    public bool NamespaceIndexReady { get; init; }
    public bool MemberIndexReady { get; init; }
    public int TypeCount { get; init; }
    public int NamespaceCount { get; init; }
    public int IndexedMemberCount { get; init; }
}
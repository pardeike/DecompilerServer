using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace DecompilerServer.Services;

/// <summary>
/// Manages the loaded assembly context, including PEFile, TypeSystem, and CSharpDecompiler instances.
/// Handles assembly loading, resolver configuration, and basic indexing.
/// </summary>
public class AssemblyContextManager : IDisposable
{
    private PEFile? _peFile;
    private ICompilation? _compilation;
    private CSharpDecompiler? _decompiler;
    private UniversalAssemblyResolver? _resolver;
    private bool _disposed;

    public bool IsLoaded => _peFile != null && _compilation != null && _decompiler != null;
    public string? AssemblyPath { get; private set; }
    public string? Mvid { get; private set; }
    public DateTime? LoadedAtUtc { get; private set; }

    // Basic statistics
    public int TypeCount => _compilation?.MainModule.TypeDefinitions.Count() ?? 0;
    public int NamespaceCount => GetNamespaces().Count();

    /// <summary>
    /// Load an assembly and initialize the decompilation context
    /// </summary>
    public void LoadAssembly(string gameDir, string assemblyFile, string[]? additionalSearchDirs = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AssemblyContextManager));

        // Dispose existing context
        DisposeContext();

        // Resolve assembly path
        var assemblyPath = Path.IsPathRooted(assemblyFile)
            ? assemblyFile
            : Path.Combine(gameDir, assemblyFile);

        if (!File.Exists(assemblyPath))
        {
            // Try common Unity paths
            var managedDir = Path.Combine(gameDir, "Managed");
            if (Directory.Exists(managedDir))
            {
                assemblyPath = Path.Combine(managedDir, Path.GetFileName(assemblyFile));
            }
        }

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

        // Create decompiler with default settings
        _decompiler = new CSharpDecompiler(_peFile, _resolver, new DecompilerSettings());

        // Extract MVID
        Mvid = _peFile.Metadata.GetModuleDefinition().Mvid.ToString();

        AssemblyPath = assemblyPath;
        LoadedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Get the current decompiler instance
    /// </summary>
    public CSharpDecompiler GetDecompiler()
    {
        EnsureLoaded();
        return _decompiler!;
    }

    /// <summary>
    /// Get the current compilation/type system
    /// </summary>
    public ICompilation GetCompilation()
    {
        EnsureLoaded();
        return _compilation!;
    }

    /// <summary>
    /// Get the current PEFile
    /// </summary>
    public PEFile GetPEFile()
    {
        EnsureLoaded();
        return _peFile!;
    }

    /// <summary>
    /// Get all namespaces in the assembly
    /// </summary>
    public IEnumerable<string> GetNamespaces()
    {
        if (!IsLoaded) return Enumerable.Empty<string>();

        return _compilation!.MainModule.TypeDefinitions
            .Select(t => t.Namespace)
            .Where(ns => !string.IsNullOrEmpty(ns))
            .Distinct()
            .OrderBy(ns => ns);
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
    /// Update decompiler settings
    /// </summary>
    public void UpdateSettings(DecompilerSettings settings)
    {
        if (!IsLoaded) return;
        _decompiler = new CSharpDecompiler(_peFile!, _resolver!, settings);
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
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            DisposeContext();
            _disposed = true;
        }
    }
}
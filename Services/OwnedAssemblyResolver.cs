using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler.Metadata;

namespace DecompilerServer.Services;

internal sealed class OwnedAssemblyResolver : IAssemblyResolver, IDisposable
{
    private const PEStreamOptions StreamOptions = PEStreamOptions.PrefetchEntireImage;

    private readonly UniversalAssemblyResolver _resolver;
    private readonly ConcurrentDictionary<string, Lazy<MetadataFile?>> _assemblies = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<MetadataFile?>> _modules = new(StringComparer.Ordinal);
    private bool _disposed;

    public OwnedAssemblyResolver(string mainAssemblyFileName)
    {
        _resolver = new UniversalAssemblyResolver(
            mainAssemblyFileName,
            throwOnError: false,
            targetFramework: null,
            runtimePack: null,
            streamOptions: StreamOptions,
            metadataOptions: MetadataReaderOptions.Default);
    }

    internal int ResolvedFileCount => _assemblies.Values.Count(value => value.IsValueCreated && value.Value != null)
        + _modules.Values.Count(value => value.IsValueCreated && value.Value != null);

    internal long ResolvedImageBytes => _assemblies.Values.Concat(_modules.Values).Where(value => value.IsValueCreated)
        .Select(value => value.Value).OfType<PEFile>().Distinct().Sum(file => (long)file.Reader.GetEntireImage().Length);

    internal static PEStreamOptions ResolvedAssemblyStreamOptions => StreamOptions;

    public void AddSearchDirectory(string directory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _resolver.AddSearchDirectory(directory);
    }

    public MetadataFile? Resolve(IAssemblyReference reference)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _assemblies.GetOrAdd(
            reference.FullName,
            _ => new Lazy<MetadataFile?>(
                () => _resolver.Resolve(reference),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public IDisposable? BeginSnapshot() => _resolver.BeginSnapshot();

    public Task<MetadataFile?> ResolveAsync(IAssemblyReference reference)
    {
        return Task.FromResult(Resolve(reference));
    }

    public MetadataFile? ResolveModule(MetadataFile mainModule, string moduleName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var key = $"{mainModule.FileName}\0{moduleName}";
        return _modules.GetOrAdd(
            key,
            _ => new Lazy<MetadataFile?>(
                () => _resolver.ResolveModule(mainModule, moduleName),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    public Task<MetadataFile?> ResolveModuleAsync(MetadataFile mainModule, string moduleName)
    {
        return Task.FromResult(ResolveModule(mainModule, moduleName));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        var resolvedFiles = _assemblies.Values
            .Concat(_modules.Values)
            .Where(value => value.IsValueCreated)
            .Select(value => value.Value)
            .OfType<IDisposable>()
            .Distinct()
            .ToList();

        _assemblies.Clear();
        _modules.Clear();

        foreach (var resolvedFile in resolvedFiles)
            resolvedFile.Dispose();

        _disposed = true;
    }
}

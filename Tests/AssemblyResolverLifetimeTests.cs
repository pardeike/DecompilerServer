using System.Reflection.PortableExecutable;
using DecompilerServer.Services;
using ICSharpCode.Decompiler.Metadata;

namespace Tests;

public class AssemblyResolverLifetimeTests
{
    [Fact]
    public void ResolvedAssemblies_ArePrefetchedCachedAndOwned()
    {
        var assemblyPath = typeof(AssemblyContextManager).Assembly.Location;
        using var mainModule = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage);
        var reference = Assert.Single(mainModule.AssemblyReferences, candidate => candidate.Name == "ICSharpCode.Decompiler");
        var resolver = new OwnedAssemblyResolver(assemblyPath);

        var first = resolver.Resolve(reference);
        var second = resolver.Resolve(reference);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, resolver.ResolvedFileCount);
        Assert.Equal(PEStreamOptions.PrefetchEntireImage, OwnedAssemblyResolver.ResolvedAssemblyStreamOptions);

        resolver.Dispose();

        Assert.Equal(0, resolver.ResolvedFileCount);
        Assert.Throws<ObjectDisposedException>(() => resolver.Resolve(reference));
    }

    [Fact]
    public void BeginSnapshot_ForwardsToUnderlyingResolverAndKeepsResolutionsCached()
    {
        var assemblyPath = typeof(AssemblyContextManager).Assembly.Location;
        using var mainModule = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage);
        var reference = Assert.Single(mainModule.AssemblyReferences, candidate => candidate.Name == "ModelContextProtocol");
        var resolver = new OwnedAssemblyResolver(assemblyPath);

        using (var snapshot = resolver.BeginSnapshot())
        {
            Assert.NotNull(snapshot);

            var first = resolver.Resolve(reference);
            var second = resolver.Resolve(reference);

            Assert.NotNull(first);
            Assert.Same(first, second);
        }

        // scope закрыт -- резолвер продолжает работать и отдаёт тот же экземпляр
        Assert.Same(resolver.Resolve(reference), resolver.Resolve(reference));

        resolver.Dispose();
    }

    [Fact]
    public void BeginSnapshot_ReturnsAFreshScopePerCall()
    {
        var assemblyPath = typeof(AssemblyContextManager).Assembly.Location;
        var resolver = new OwnedAssemblyResolver(assemblyPath);

        using var first = resolver.BeginSnapshot();
        using var second = resolver.BeginSnapshot();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);

        // закрытие scope не выводит резолвер из строя
        first.Dispose();

        resolver.Dispose();
    }
}

using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer.Services;

/// <summary>
/// Keys type definitions by <see cref="IType.ReflectionName"/> for cross-context comparison.
/// <see cref="IType.FullName"/> drops generic arity, so <c>N.IFoo</c> and <c>N.IFoo`1</c> collide.
/// ReflectionName keeps the arity and uses <c>+</c> for nested types.
/// Malformed or obfuscated metadata can still repeat a ReflectionName. Repeats get a
/// <c>#2</c>, <c>#3</c> suffix in metadata order and are reported as collisions.
/// </summary>
internal static class TypeKeyIndex
{
    public static Dictionary<string, ITypeDefinition> Build(
        IEnumerable<ITypeDefinition> types,
        out IReadOnlyList<TypeKeyCollision> collisions)
    {
        var map = new Dictionary<string, ITypeDefinition>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            var key = GetKey(type);
            var occurrence = occurrences.GetValueOrDefault(key) + 1;
            var candidate = occurrence == 1 ? key : $"{key}#{occurrence}";
            while (!map.TryAdd(candidate, type))
                candidate = $"{key}#{++occurrence}";
            occurrences[key] = occurrence;
        }

        collisions = occurrences
            .Where(pair => pair.Value > 1)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new TypeKeyCollision(pair.Key, pair.Value))
            .ToList();
        return map;
    }

    public static string GetKey(ITypeDefinition type) =>
        string.IsNullOrEmpty(type.ReflectionName) ? type.FullName : type.ReflectionName;
}

public sealed record TypeKeyCollision(string TypeName, int Count);

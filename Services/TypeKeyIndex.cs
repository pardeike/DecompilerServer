using ICSharpCode.Decompiler.TypeSystem;

namespace DecompilerServer.Services;

/// <summary>
/// Builds lookup dictionaries for type definitions keyed by a name that is safe for ordinal
/// comparison.
/// <para>
/// <see cref="IType.FullName"/> cannot be used as a dictionary key: ILSpy's type system drops the
/// generic arity, so <c>Topomatic.Alg.Parameters.IParameterTable</c> and
/// <c>Topomatic.Alg.Parameters.IParameterTable`1</c> both report the same FullName and
/// <c>ToDictionary</c> throws <c>ArgumentException: An item with the same key has already been
/// added</c>. That is not an exotic case - 34 of the Topomatic.* assemblies in the Robur corpus
/// contain at least one such pair, which makes <c>compare_contexts</c> unusable on exactly the
/// assemblies we care about.
/// </para>
/// <para>
/// <see cref="IType.ReflectionName"/> keeps the arity suffix (and encodes nested types with
/// <c>/</c>), so it is unique in practice. Obfuscated assemblies can still produce collisions
/// (ReactObfuscator replaces invalid identifier characters with a small set of glyphs, so two
/// distinct metadata names can map onto the same readable string). Those are disambiguated by a
/// stable <c>#2</c>, <c>#3</c> suffix based on metadata order, and every applied disambiguation is
/// reported to the caller so a diff never silently drops a type.
/// </para>
/// </summary>
internal static class TypeKeyIndex
{
    /// <summary>
    /// Maps each type to its key. Duplicates receive a <c>#N</c> suffix and are appended to
    /// <paramref name="collisions"/> in the form <c>key (resolved as: A, B)</c>.
    /// </summary>
    public static Dictionary<string, ITypeDefinition> Build(
        IEnumerable<ITypeDefinition> types,
        List<string> collisions)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(collisions);

        var map = new Dictionary<string, ITypeDefinition>(StringComparer.Ordinal);
        var claimed = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            if (type is null)
                continue;

            var key = GetKey(type);
            if (map.TryAdd(key, type))
                continue;

            // Already present: disambiguate both sides deterministically.
            var existing = map[key];
            var members = new List<string> { existing.Name, type.Name };
            var suffix = 2;
            string uniqueKey;
            while (true)
            {
                uniqueKey = $"{key}#{suffix++}";
                if (map.TryAdd(uniqueKey, type))
                    break;
                var previous = map[uniqueKey];
                members.Add(previous.Name);
            }

            claimed[key] = members;
        }

        collisions.AddRange(claimed
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key} (resolved as: {string.Join(", ", pair.Value)})"));

        return map;
    }

    /// <summary>
    /// Renders collision diagnostics for a tool response, or <c>null</c> when there were none.
    /// </summary>
    public static string? DescribeCollisions(IEnumerable<string> left, IEnumerable<string> right)
    {
        var parts = new List<string>(2);
        Add("left", left);
        Add("right", right);

        if (parts.Count == 0)
            return null;

        return string.Join("; ", parts);

        void Add(string side, IEnumerable<string> collisions)
        {
            var list = collisions.ToList();
            if (list.Count > 0)
                parts.Add($"{side} name collisions disambiguated: {string.Join("; ", list)}");
        }
    }

    private static string GetKey(ITypeDefinition type)
    {
        var reflectionName = type.ReflectionName;
        return string.IsNullOrEmpty(reflectionName) ? type.FullName : reflectionName;
    }
}

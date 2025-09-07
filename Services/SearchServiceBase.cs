using ICSharpCode.Decompiler.TypeSystem;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace DecompilerServer.Services;

/// <summary>
/// Base class for search services providing common pagination and filtering functionality.
/// Supports cursor-based pagination, various search modes, and result caching.
/// </summary>
public abstract class SearchServiceBase
{
    protected readonly AssemblyContextManager _contextManager;
    protected readonly MemberResolver _memberResolver;
    private readonly ConcurrentDictionary<string, SearchResult<MemberSummary>> _searchCache = new();

    protected SearchServiceBase(AssemblyContextManager contextManager, MemberResolver memberResolver)
    {
        _contextManager = contextManager;
        _memberResolver = memberResolver;
    }

    /// <summary>
    /// Search types with filters and pagination (cached)
    /// </summary>
    public SearchResult<MemberSummary> SearchTypes(
        string query,
        bool regex = false,
        string? namespaceFilter = null,
        bool includeNested = true,
        int limit = 50,
        string? cursor = null)
    {
        // Create cache key from search parameters
        var cacheKey = $"types:{query}:{regex}:{namespaceFilter}:{includeNested}:{limit}:{cursor}";

        // Check cache first
        if (_searchCache.TryGetValue(cacheKey, out var cachedResult))
            return cachedResult;

        var compilation = _contextManager.GetCompilation();
        var allTypes = _contextManager.GetAllTypes();

        // Apply filters using optimized indexes when possible
        var filteredTypes = FilterTypes(allTypes, query, regex, namespaceFilter, includeNested);

        // Apply pagination
        var results = ApplyPagination(filteredTypes, limit, cursor, type => CreateTypeSummary(type));

        // Cache the result
        _searchCache.TryAdd(cacheKey, results);

        return results;
    }

    private IEnumerable<ITypeDefinition> FilterTypes(
        IEnumerable<ITypeDefinition> types,
        string query,
        bool regex,
        string? namespaceFilter,
        bool includeNested)
    {
        // Use optimized namespace filtering when possible
        if (!string.IsNullOrEmpty(namespaceFilter))
        {
            types = _contextManager.GetTypesInNamespace(namespaceFilter);
        }

        return types.Where(type =>
        {
            // Nested type filter
            if (!includeNested && type.DeclaringType != null)
                return false;

            // Name filter
            return MatchesQuery(type.Name, query, regex);
        });
    }

    /// <summary>
    /// Search members with rich filtering (cached)
    /// </summary>
    public SearchResult<MemberSummary> SearchMembers(
        string query,
        bool regex = false,
        string? namespaceFilter = null,
        string? declaringTypeFilter = null,
        string? attributeFilter = null,
        string? returnTypeFilter = null,
        string[]? paramTypeFilters = null,
        string? kind = null,
        string? accessibility = null,
        bool? isStatic = null,
        bool? isAbstract = null,
        bool? isVirtual = null,
        int? genericArity = null,
        int limit = 50,
        string? cursor = null)
    {
        // Create cache key from search parameters
        var paramTypesKey = paramTypeFilters != null ? string.Join(",", paramTypeFilters) : "";
        var cacheKey = $"members:{query}:{regex}:{namespaceFilter}:{declaringTypeFilter}:{attributeFilter}:{returnTypeFilter}:{paramTypesKey}:{kind}:{accessibility}:{isStatic}:{isAbstract}:{isVirtual}:{genericArity}:{limit}:{cursor}";

        // Check cache first
        if (_searchCache.TryGetValue(cacheKey, out var cachedResult))
            return cachedResult;

        var compilation = _contextManager.GetCompilation();

        // Use optimized type filtering when possible
        IEnumerable<ITypeDefinition> types;
        if (!string.IsNullOrEmpty(namespaceFilter))
        {
            types = _contextManager.GetTypesInNamespace(namespaceFilter);
        }
        else
        {
            types = _contextManager.GetAllTypes();
        }

        var members = types.SelectMany(type => GetAllMembers(type));

        // Apply filters
        var filteredMembers = FilterMembers(members, query, regex, declaringTypeFilter, attributeFilter,
            returnTypeFilter, paramTypeFilters, kind, accessibility, isStatic, isAbstract, isVirtual, genericArity);

        var results = ApplyPagination(filteredMembers, limit, cursor, member => CreateMemberSummary(member));

        // Cache the result
        _searchCache.TryAdd(cacheKey, results);

        return results;
    }

    private IEnumerable<IMember> FilterMembers(
        IEnumerable<IMember> members,
        string query,
        bool regex,
        string? declaringTypeFilter,
        string? attributeFilter,
        string? returnTypeFilter,
        string[]? paramTypeFilters,
        string? kind,
        string? accessibility,
        bool? isStatic,
        bool? isAbstract,
        bool? isVirtual,
        int? genericArity)
    {
        return members.Where(member =>
        {
            // Declaring type filter
            if (!string.IsNullOrEmpty(declaringTypeFilter) && !MatchesQuery(member.DeclaringType?.Name ?? "", declaringTypeFilter, false))
                return false;

            // Kind filter
            if (!string.IsNullOrEmpty(kind) && !MatchesKind(member, kind))
                return false;

            // Accessibility filter
            if (!string.IsNullOrEmpty(accessibility) && !MatchesAccessibility(member, accessibility))
                return false;

            // Static filter
            if (isStatic.HasValue && member.IsStatic != isStatic.Value)
                return false;

            // Abstract filter
            if (isAbstract.HasValue && member.IsAbstract != isAbstract.Value)
                return false;

            // Virtual filter
            if (isVirtual.HasValue && member.IsVirtual != isVirtual.Value)
                return false;

            // Return type filter
            if (!string.IsNullOrEmpty(returnTypeFilter) && !MatchesReturnType(member, returnTypeFilter))
                return false;

            // Parameter type filters
            if (paramTypeFilters != null && !MatchesParameterTypes(member, paramTypeFilters))
                return false;

            // Generic arity filter
            if (genericArity.HasValue && !MatchesGenericArity(member, genericArity.Value))
                return false;

            // Attribute filter
            if (!string.IsNullOrEmpty(attributeFilter) && !HasAttribute(member, attributeFilter))
                return false;

            // Name filter
            return MatchesQuery(member.Name, query, regex);
        });
    }

    /// <summary>
    /// Clear the search cache
    /// </summary>
    public void ClearSearchCache()
    {
        _searchCache.Clear();
    }

    /// <summary>
    /// Get search cache statistics
    /// </summary>
    public SearchCacheStats GetSearchCacheStats()
    {
        return new SearchCacheStats
        {
            CachedSearches = _searchCache.Count,
            TotalResults = _searchCache.Values.Sum(r => r.Items.Count),
            AverageResultsPerSearch = _searchCache.Any() ? _searchCache.Values.Average(r => r.Items.Count) : 0
        };
    }

    /// <summary>
    /// Create pagination helper for large result sets
    /// </summary>
    protected SearchResult<T> ApplyPagination<TSource, T>(
        IEnumerable<TSource> source,
        int limit,
        string? cursor,
        Func<TSource, T> selector)
    {
        var items = source.ToList();
        var startIndex = 0;

        // Parse cursor if provided
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var pageItems = items
            .Skip(startIndex)
            .Take(limit)
            .Select(selector)
            .ToList();

        var hasMore = startIndex + limit < items.Count;
        var nextCursor = hasMore ? (startIndex + limit).ToString() : null;

        return new SearchResult<T>
        {
            Items = pageItems,
            HasMore = hasMore,
            NextCursor = nextCursor,
            TotalEstimate = items.Count
        };
    }

    protected bool MatchesQuery(string text, string query, bool regex)
    {
        if (string.IsNullOrEmpty(query))
            return true;

        if (regex)
        {
            try
            {
                return Regex.IsMatch(text, query, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
            }
            catch
            {
                return false; // Invalid regex or timeout
            }
        }

        return text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    protected IEnumerable<IMember> GetAllMembers(ITypeDefinition type)
    {
        return type.Methods.Cast<IMember>()
            .Concat(type.Fields)
            .Concat(type.Properties)
            .Concat(type.Events);
    }

    protected bool MatchesKind(IMember member, string kind)
    {
        var memberKind = member switch
        {
            IMethod => "method",
            IField => "field",
            IProperty => "property",
            IEvent => "event",
            _ => "unknown"
        };

        return string.Equals(memberKind, kind, StringComparison.OrdinalIgnoreCase);
    }

    protected bool MatchesAccessibility(IMember member, string accessibility)
    {
        var memberAccessibility = member.Accessibility.ToString().ToLowerInvariant();
        return string.Equals(memberAccessibility, accessibility, StringComparison.OrdinalIgnoreCase);
    }

    protected bool MatchesReturnType(IMember member, string returnTypeFilter)
    {
        var returnType = member.ReturnType?.Name ?? "";
        return MatchesQuery(returnType, returnTypeFilter, false);
    }

    protected bool MatchesParameterTypes(IMember member, string[] paramTypeFilters)
    {
        if (member is not IMethod method)
            return false;

        var paramTypes = method.Parameters.Select(p => p.Type.Name).ToArray();

        // Check if all filters match any parameter type
        return paramTypeFilters.All(filter =>
            paramTypes.Any(paramType => MatchesQuery(paramType, filter, false)));
    }

    protected bool MatchesGenericArity(IMember member, int genericArity)
    {
        return member switch
        {
            IMethod method => method.TypeParameters.Count == genericArity,
            IType type => type.TypeParameters.Count == genericArity,
            _ => genericArity == 0
        };
    }

    protected bool HasAttribute(IMember member, string attributeFilter)
    {
        return member.GetAttributes().Any(attr =>
            attr.AttributeType.FullName.Contains(attributeFilter, StringComparison.OrdinalIgnoreCase));
    }

    protected MemberSummary CreateTypeSummary(ITypeDefinition type)
    {
        return new MemberSummary
        {
            MemberId = _memberResolver.GenerateMemberId(type),
            Name = type.Name,
            FullName = type.FullName,
            Kind = "Type",
            DeclaringType = type.DeclaringType?.FullName,
            Namespace = type.Namespace,
            Signature = _memberResolver.GetMemberSignature(type),
            Accessibility = type.Accessibility.ToString(),
            IsStatic = type.IsStatic,
            IsAbstract = type.IsAbstract
        };
    }

    protected MemberSummary CreateMemberSummary(IMember member)
    {
        return new MemberSummary
        {
            MemberId = _memberResolver.GenerateMemberId(member),
            Name = member.Name,
            FullName = member.FullName,
            Kind = GetMemberKind(member),
            DeclaringType = member.DeclaringType?.FullName,
            Namespace = member.DeclaringType?.Namespace,
            Signature = _memberResolver.GetMemberSignature(member),
            Accessibility = member.Accessibility.ToString(),
            IsStatic = member.IsStatic,
            IsAbstract = member.IsAbstract,
            IsVirtual = member.IsVirtual
        };
    }

    private string GetMemberKind(IMember member)
    {
        return member switch
        {
            IMethod method when method.IsConstructor => "Constructor",
            IMethod => "Method",
            IField => "Field",
            IProperty => "Property",
            IEvent => "Event",
            _ => "Unknown"
        };
    }
}

/// <summary>
/// Search cache statistics
/// </summary>
public class SearchCacheStats
{
    public int CachedSearches { get; init; }
    public int TotalResults { get; init; }
    public double AverageResultsPerSearch { get; init; }
}

/// <summary>
/// Search result with pagination support
/// </summary>
public class SearchResult<T>
{
    public required List<T> Items { get; init; }
    public required bool HasMore { get; init; }
    public required string? NextCursor { get; init; }
    public required int TotalEstimate { get; init; }
}

/// <summary>
/// Summary information about a member
/// </summary>
public class MemberSummary
{
    public required string MemberId { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required string Kind { get; init; }
    public required string? DeclaringType { get; init; }
    public required string? Namespace { get; init; }
    public required string Signature { get; init; }
    public required string Accessibility { get; init; }
    public required bool IsStatic { get; init; }
    public required bool IsAbstract { get; init; }
    public bool IsVirtual { get; init; }
    public string? Summary { get; init; }
}
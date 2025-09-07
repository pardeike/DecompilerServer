using ICSharpCode.Decompiler.TypeSystem;
using System.Reflection.Metadata;

namespace DecompilerServer.Services;

/// <summary>
/// Analyzes IL code to find usage patterns, callers, and callees.
/// Provides functionality for finding where members are used across the assembly.
/// </summary>
public class UsageAnalyzer
{
    private readonly AssemblyContextManager _contextManager;
    private readonly MemberResolver _memberResolver;

    public UsageAnalyzer(AssemblyContextManager contextManager, MemberResolver memberResolver)
    {
        _contextManager = contextManager;
        _memberResolver = memberResolver;
    }

    /// <summary>
    /// Find all usages of a member across the assembly
    /// </summary>
    public IEnumerable<UsageReference> FindUsages(string memberId, int limit = 100, string? cursor = null)
    {
        var targetMember = _memberResolver.ResolveMember(memberId);
        if (targetMember == null)
            return Enumerable.Empty<UsageReference>();

        var usages = new List<UsageReference>();
        var compilation = _contextManager.GetCompilation();
        var allTypes = _contextManager.GetAllTypes();

        // Parse cursor for pagination
        var startIndex = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var processedCount = 0;
        var foundCount = 0;

        foreach (var type in allTypes)
        {
            if (foundCount >= limit) break;

            foreach (var method in type.Methods)
            {
                processedCount++;
                if (processedCount <= startIndex) continue;
                if (foundCount >= limit) break;

                var methodUsages = FindUsagesInMethod(method, targetMember);
                foreach (var usage in methodUsages)
                {
                    if (foundCount >= limit) break;
                    usages.Add(usage);
                    foundCount++;
                }
            }
        }

        return usages;
    }

    /// <summary>
    /// Find direct callers of a method
    /// </summary>
    public IEnumerable<UsageReference> FindCallers(string methodId, int limit = 100, string? cursor = null)
    {
        var usages = FindUsages(methodId, limit, cursor);
        return usages.Where(u => u.Kind == UsageKind.Call || u.Kind == UsageKind.NewObject);
    }

    /// <summary>
    /// Find what methods/members a method calls (callees)
    /// </summary>
    public IEnumerable<UsageReference> FindCallees(string methodId, int limit = 100, string? cursor = null)
    {
        var method = _memberResolver.ResolveMethod(methodId);
        if (method == null)
            return Enumerable.Empty<UsageReference>();

        var callees = new List<UsageReference>();

        // This is simplified - full implementation would analyze method body IL
        // to find all call/callvirt/newobj instructions and resolve their targets

        // For now, return empty - full implementation would require IL analysis
        return callees;
    }

    /// <summary>
    /// Find string literals in a method
    /// </summary>
    public IEnumerable<string> FindStringLiteralsInMethod(IMethod method)
    {
        // Simplified implementation - would analyze IL for ldstr instructions
        // For now return empty - full implementation requires IL analysis
        return Enumerable.Empty<string>();
    }

    /// <summary>
    /// Find all string literals in the assembly
    /// </summary>
    public IEnumerable<StringLiteralReference> FindStringLiterals(string query, bool regex = false, int limit = 100, string? cursor = null)
    {
        var results = new List<StringLiteralReference>();
        var compilation = _contextManager.GetCompilation();
        var allTypes = _contextManager.GetAllTypes();

        // Parse cursor for pagination
        var startIndex = 0;
        if (!string.IsNullOrEmpty(cursor) && int.TryParse(cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var processedCount = 0;
        var foundCount = 0;

        foreach (var type in allTypes)
        {
            if (foundCount >= limit) break;

            foreach (var method in type.Methods)
            {
                processedCount++;
                if (processedCount <= startIndex) continue;
                if (foundCount >= limit) break;

                var literals = FindStringLiteralsInMethod(method);
                foreach (var literal in literals)
                {
                    if (foundCount >= limit) break;

                    if (MatchesStringQuery(literal, query, regex))
                    {
                        results.Add(new StringLiteralReference
                        {
                            Value = literal,
                            ContainingMember = _memberResolver.GenerateMemberId(method),
                            ContainingType = method.DeclaringType?.FullName ?? "",
                            Line = null // Would need source mapping for line numbers
                        });
                        foundCount++;
                    }
                }
            }
        }

        return results;
    }

    private IEnumerable<UsageReference> FindUsagesInMethod(IMethod method, IEntity targetMember)
    {
        // Simplified implementation - full version would analyze IL instructions
        // to find references to the target member via metadata tokens

        // For now, return empty list - full implementation requires:
        // 1. Getting method body IL
        // 2. Parsing IL instructions (call, callvirt, ldfld, stfld, newobj, etc.)
        // 3. Resolving metadata tokens to member references
        // 4. Comparing with target member

        return Enumerable.Empty<UsageReference>();
    }

    private bool MatchesStringQuery(string text, string query, bool regex)
    {
        if (string.IsNullOrEmpty(query))
            return true;

        if (regex)
        {
            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(text, query,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(1));
            }
            catch
            {
                return false;
            }
        }

        return text.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Represents a usage of a member
/// </summary>
public class UsageReference
{
    public required string InMember { get; init; }
    public required string InType { get; init; }
    public required UsageKind Kind { get; init; }
    public int? Line { get; init; }
    public string? Snippet { get; init; }
}

/// <summary>
/// Types of member usage
/// </summary>
public enum UsageKind
{
    Call,
    FieldRead,
    FieldWrite,
    PropertyRead,
    PropertyWrite,
    NewObject,
    TypeReference
}

/// <summary>
/// Represents a string literal reference
/// </summary>
public class StringLiteralReference
{
    public required string Value { get; init; }
    public required string ContainingMember { get; init; }
    public required string ContainingType { get; init; }
    public int? Line { get; init; }
}
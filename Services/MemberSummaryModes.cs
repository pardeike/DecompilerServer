namespace DecompilerServer.Services;

public enum MemberSummaryMode
{
    Ids,
    Discovery,
    Signatures,
    Full
}

public static class MemberSummaryModes
{
    public const int MaxLimit = 100;

    public static MemberSummaryMode Parse(string? mode, MemberSummaryMode defaultMode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return defaultMode;

        return mode.Trim().ToLowerInvariant() switch
        {
            "ids" or "id" => MemberSummaryMode.Ids,
            "discovery" or "discover" or "browse" => MemberSummaryMode.Discovery,
            "signatures" or "signature" or "surface" => MemberSummaryMode.Signatures,
            "full" or "verbose" => MemberSummaryMode.Full,
            _ => throw new ArgumentException($"Invalid mode: {mode}. Use 'ids', 'discovery', 'signatures', or 'full'.")
        };
    }

    public static int ClampLimit(int limit, int defaultLimit)
    {
        if (limit <= 0)
            return defaultLimit;

        return Math.Min(limit, MaxLimit);
    }

    public static SearchResult<object> Project(SearchResult<MemberSummary> result, MemberSummaryMode mode)
    {
        var projectedItems = result.Items
            .Select(item => Project(item, mode))
            .Cast<object>()
            .ToList();

        return new SearchResult<object>(projectedItems, result.HasMore, result.NextCursor, result.TotalEstimate);
    }

    public static object Project(MemberSummary summary, MemberSummaryMode mode)
    {
        return mode switch
        {
            MemberSummaryMode.Ids => new
            {
                memberId = summary.MemberId,
                name = summary.Name,
                kind = summary.Kind
            },
            MemberSummaryMode.Discovery => new
            {
                memberId = summary.MemberId,
                name = summary.Name,
                fullName = summary.FullName,
                kind = summary.Kind,
                declaringType = summary.DeclaringType,
                @namespace = summary.Namespace
            },
            MemberSummaryMode.Signatures => new
            {
                memberId = summary.MemberId,
                name = summary.Name,
                kind = summary.Kind,
                signature = summary.Signature,
                accessibility = summary.Accessibility,
                isStatic = summary.IsStatic,
                isAbstract = summary.IsAbstract,
                isVirtual = summary.IsVirtual
            },
            _ => summary
        };
    }
}

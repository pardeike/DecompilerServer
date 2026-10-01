namespace DecompilerServer.Services;

/// <summary>
/// User-selected output format, fixed for the lifetime of the server.
/// </summary>
public sealed record ResponseFormatOptions(bool Extended)
{
    public const string EnvironmentVariable = "DECOMPILER_RESPONSE_MODE";

    public static ResponseFormatOptions FromEnvironment() =>
        Parse(Environment.GetEnvironmentVariable(EnvironmentVariable));

    internal static ResponseFormatOptions Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "compact" => new(false),
        "extended" => new(true),
        _ => throw new ArgumentException($"{EnvironmentVariable} must be 'compact' or 'extended'.")
    };
}

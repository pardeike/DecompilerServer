namespace DecompilerServer.Services;

public sealed class ToolErrorException : Exception
{
    public ToolErrorException(
        string code,
        string message,
        object? details = null,
        IEnumerable<ToolErrorHint>? hints = null)
        : base(message)
    {
        Code = code;
        DetailsPayload = details;
        Hints = hints?.ToArray() ?? Array.Empty<ToolErrorHint>();
    }

    public string Code { get; }
    public object? DetailsPayload { get; }
    public IReadOnlyList<ToolErrorHint> Hints { get; }
}

public sealed record ToolErrorHint(
    string Tool,
    object Arguments,
    string? Reason = null);

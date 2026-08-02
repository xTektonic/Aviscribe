namespace Aviscribe.Core.Capture;

/// <summary>
/// Optional platform information used while opening a capture source.
/// </summary>
public sealed record CaptureOpenOptions
{
    public static CaptureOpenOptions Default { get; } = new();

    /// <summary>
    /// Desktop-portal parent window identifier, such as an X11 XID. An empty
    /// value is valid and lets the portal show an unparented chooser.
    /// </summary>
    public string? ParentWindowIdentifier { get; init; }
}

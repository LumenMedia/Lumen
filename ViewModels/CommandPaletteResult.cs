namespace Lumen.ViewModels;

public sealed class CommandPaletteResult
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Glyph { get; init; } = "\uE721";
    public string? ItemId { get; init; }
    public string? ItemType { get; init; }
    public string? ImageUrl { get; init; }
    public string? Command { get; init; }
}

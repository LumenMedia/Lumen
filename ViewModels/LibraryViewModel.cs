using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Models;

namespace Lumen.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Id { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Type { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string? PrimaryImageTag { get; set; }
    [ObservableProperty]
    public partial int? ProductionYear { get; set; }

    public static LibraryViewModel FromDto(BaseItemDto dto) => new()
    {
        Id = dto.Id ?? string.Empty,
        Name = dto.Name ?? "Library",
        Type = dto.CollectionType ?? dto.Type ?? string.Empty,
        PrimaryImageTag = dto.PrimaryImageTag
    };
}

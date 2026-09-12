using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Services;

namespace Lumen.ViewModels;

public enum BrowseKind { Genre, Collection }

public sealed record BrowseNavigationParameter(BrowseKind Kind, string Value, string Title);

public partial class BrowsePageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;
    public ObservableCollection<MediaCardViewModel> Items { get; } = [];

    public async Task LoadAsync(BrowseNavigationParameter parameter, CancellationToken ct = default)
    {
        Title = parameter.Title;
        Subtitle = parameter.Kind == BrowseKind.Genre ? "Genre" : "Collection";
        Items.Clear();
        StatusMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = parameter.Kind == BrowseKind.Genre
                ? await App.Jellyfin.GetItemsByGenreAsync(parameter.Value, 300, ct)
                : await App.Jellyfin.GetCollectionItemsAsync(parameter.Value, ct);

            foreach (var item in result.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
            {
                var card = MediaCardViewModel.FromDto(item);
                card.ImageUrl = App.Jellyfin.GetImageUrl(item.Id!, "Primary",
                    item.ImageTags?.GetValueOrDefault("Primary") ?? item.PrimaryImageTag, 420, 88);
                Items.Add(card);
            }

            if (Items.Count == 0) StatusMessage = "Nothing to show here yet.";
        }
        catch (Exception ex) { StatusMessage = ConnectionErrorMapper.ToFriendlyMessage(ex, App.MainViewModel.ServerUrl); }
        finally { IsBusy = false; }
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumen.Services;

namespace Lumen.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool HasSearched { get; set; }

    public ObservableCollection<MediaCardViewModel> Results { get; } = [];

    public async Task SearchAsync(CancellationToken ct = default)
    {
        if (!App.Jellyfin.IsAuthenticated) return;
        var text = Query.Trim();
        if (text.Length < 2)
        {
            Results.Clear();
            HasSearched = false;
            StatusMessage = string.Empty;
            return;
        }

        IsBusy = true;
        HasSearched = true;
        StatusMessage = string.Empty;
        try
        {
            var result = await App.Jellyfin.SearchItemsAsync(text, 60, ct);
            Results.Clear();
            foreach (var item in result.Items.Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Name)))
            {
                var card = MediaCardViewModel.FromDto(item);
                card.ImageUrl = App.Jellyfin.GetImageUrl(item.Id!, "Primary", item.PrimaryImageTag, 360, 90);
                Results.Add(card);
            }

            if (Results.Count == 0)
                StatusMessage = $"No results found for “{text}”.";
        }
        catch (Exception ex) { StatusMessage = ConnectionErrorMapper.ToFriendlyMessage(ex, App.MainViewModel.ServerUrl); }
        finally { IsBusy = false; }
    }
}

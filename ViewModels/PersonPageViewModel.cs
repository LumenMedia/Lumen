using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Lumen.ViewModels;

public sealed record PersonNavigationParameter(string Id, string Name, string ImageUrl);

public partial class PersonPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }
    [ObservableProperty]
    public partial string PersonId { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string ImageUrl { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string Overview { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string LifeInfo { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty]
    public partial bool HasMovies { get; set; }
    [ObservableProperty]
    public partial bool HasShows { get; set; }

    public ObservableCollection<MediaCardViewModel> Movies { get; } = [];
    public ObservableCollection<MediaCardViewModel> Shows { get; } = [];

    public async Task LoadAsync(PersonNavigationParameter person, CancellationToken ct = default)
    {
        PersonId = person.Id;
        Name = person.Name;
        ImageUrl = person.ImageUrl;
        Movies.Clear();
        Shows.Clear();
        Overview = string.Empty;
        LifeInfo = string.Empty;
        StatusMessage = string.Empty;
        IsBusy = true;

        try
        {
            try
            {
                var details = await App.Jellyfin.GetItemAsync(person.Id, ct);
                if (details is not null)
                {
                    Name = details.Name ?? Name;
                    Overview = details.Overview ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(details.PrimaryImageTag))
                        ImageUrl = App.Jellyfin.GetImageUrl(person.Id, "Primary", details.PrimaryImageTag, 420, 90);

                    var bits = new List<string>();
                    if (details.PremiereDate is DateTime born)
                        bits.Add($"Born {born:d MMMM yyyy}");
                    if (details.ProductionLocations?.FirstOrDefault() is string place && !string.IsNullOrWhiteSpace(place))
                        bits.Add(place);
                    LifeInfo = string.Join(" • ", bits);
                }
            }
            catch { }

            var result = await App.Jellyfin.GetItemsByPersonAsync(person.Id, 300, ct);
            foreach (var item in result.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
            {
                var card = MediaCardViewModel.FromDto(item);
                card.ImageUrl = App.Jellyfin.GetImageUrl(
                    item.Id!, "Primary",
                    item.ImageTags?.GetValueOrDefault("Primary") ?? item.PrimaryImageTag,
                    420, 88);

                if (string.Equals(item.Type, "Movie", StringComparison.OrdinalIgnoreCase))
                    Movies.Add(card);
                else if (string.Equals(item.Type, "Series", StringComparison.OrdinalIgnoreCase))
                    Shows.Add(card);
            }

            HasMovies = Movies.Count > 0;
            HasShows = Shows.Count > 0;
            if (!HasMovies && !HasShows)
                StatusMessage = "No movies or shows found for this person in your library.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't load titles for {person.Name}: {ex.Message}";
        }
        finally { IsBusy = false; }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Lumen.Services;
using Lumen.ViewModels;

namespace Lumen.Views;

public sealed partial class PersonPage : Page
{
    private readonly PersonPageViewModel _viewModel = new();

    public PersonPage()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is PersonNavigationParameter person)
        {
            await _viewModel.LoadAsync(person);
            ArtworkPreloader.Preload(
                new[] { _viewModel.ImageUrl }
                    .Concat(_viewModel.Movies.Select(x => x.ImageUrl))
                    .Concat(_viewModel.Shows.Select(x => x.ImageUrl)));
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void MediaCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MediaCardViewModel item } &&
            !string.IsNullOrWhiteSpace(item.Id))
            Frame.Navigate(typeof(ItemPage), item.Id);
    }

    private void MediaCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MediaCardViewModel item)
        {
            MediaContextMenu.Show(element, item, Frame);
            e.Handled = true;
        }
    }
}

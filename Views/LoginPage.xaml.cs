using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Lumen.Services;

namespace Lumen.Views;

public sealed partial class LoginPage : Page
{
    public event EventHandler? LoginSucceeded;
    public event EventHandler? LoginCancelled;

    public LoginPage()
    {
        InitializeComponent();
        ServerBox.Text = App.Settings.ServerUrl;
        UsernameBox.Text = App.Settings.Username;
        RefreshSavedServers();
    }

    public void RefreshSavedServers()
    {
        var servers = App.Settings.RememberedServers;
        RecentServersList.ItemsSource = servers;
        RecentServersPanel.Visibility = servers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowCancel(bool visible)
        => CancelButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void PrepareForUser(string username)
    {
        ServerBox.Text = App.Settings.ServerUrl;
        UsernameBox.Text = username;
        PasswordBox.Password = string.Empty;
        UsernameBox.Focus(FocusState.Programmatic);
    }

    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        DiscoverButton.IsEnabled = false;
        DiscoveryStatus.Text = "Looking for Jellyfin servers…";
        DiscoveredServersList.Visibility = Visibility.Collapsed;
        DiscoveredServersList.ItemsSource = null;

        try
        {
            var servers = await JellyfinDiscoveryService.DiscoverAsync();
            if (servers.Count == 0)
            {
                DiscoveryStatus.Text = "No Jellyfin servers found. Make sure server discovery is enabled and UDP 7359 is reachable.";
                return;
            }

            DiscoveredServersList.ItemsSource = servers;
            DiscoveredServersList.Visibility = Visibility.Visible;
            var reachable = servers.Count(x => x.IsReachable);
            DiscoveryStatus.Text = servers.Count == 1
                ? (servers[0].IsReachable
                    ? $"Server ready • {servers[0].LatencyText}"
                    : "Server discovered, but its Jellyfin endpoint is not reachable.")
                : $"{servers.Count} Jellyfin servers found • {reachable} reachable.";

            // For a single result, prefill it immediately while still showing the
            // selectable result so the user can see what was discovered.
            if (servers.Count == 1)
                ServerBox.Text = servers[0].Address;
        }
        catch (Exception ex)
        {
            DiscoveryStatus.Text = $"Server discovery failed: {ex.Message}";
        }
        finally
        {
            DiscoverButton.IsEnabled = true;
        }
    }

    private void DiscoveredServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DiscoveredJellyfinServer server })
        {
            ServerBox.Text = server.Address;
            DiscoveryStatus.Text = server.IsReachable
                ? $"Selected {server.Name} • Ready • {server.LatencyText}"
                : $"Selected {server.Name} • Endpoint currently unreachable.";
            UsernameBox.Focus(FocusState.Programmatic);
        }
    }

    private void RememberedServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RememberedServerInfo server })
        {
            ServerBox.Text = server.Address;
            DiscoveryStatus.Text = $"Selected {server.Name}.";
            UsernameBox.Focus(FocusState.Programmatic);
        }
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        await SignInAsync();
    }

    public async Task<bool> SignInAsync()
    {
        ErrorBar.IsOpen = false;
        if (string.IsNullOrWhiteSpace(ServerBox.Text) || string.IsNullOrWhiteSpace(UsernameBox.Text))
        {
            ErrorBar.Message = "Enter your Jellyfin server and username.";
            ErrorBar.IsOpen = true;
            return false;
        }

        SignInButton.IsEnabled = false;
        Progress.IsActive = true;
        try
        {
            await App.MainViewModel.LoginAsync(ServerBox.Text, UsernameBox.Text, PasswordBox.Password, RememberMeCheck.IsChecked == true);
            PasswordBox.Password = string.Empty;
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex)
        {
            ErrorBar.Message = ConnectionErrorMapper.ToFriendlyMessage(ex, ServerBox.Text);
            ErrorBar.IsOpen = true;
            return false;
        }
        finally
        {
            Progress.IsActive = false;
            SignInButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Opening "Add account" is not a logout action. If the user changes their
        // mind, keep the currently authenticated session and return to the shell.
        ErrorBar.IsOpen = false;
        PasswordBox.Password = string.Empty;
        LoginCancelled?.Invoke(this, EventArgs.Empty);
    }
}

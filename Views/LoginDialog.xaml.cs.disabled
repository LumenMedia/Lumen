using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lumen.Views;

public sealed partial class LoginDialog : ContentDialog
{
    public LoginDialog()
    {
        InitializeComponent();
        ServerBox.Text = App.Settings.ServerUrl;
        UsernameBox.Text = App.Settings.Username;
    }

    private async void SignIn_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        ErrorBar.IsOpen = false;
        Progress.IsActive = true;
        try
        {
            await App.MainViewModel.LoginAsync(ServerBox.Text, UsernameBox.Text, PasswordBox.Password);
            Hide();
        }
        catch (Exception ex)
        {
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
        finally
        {
            Progress.IsActive = false;
        }
    }
}

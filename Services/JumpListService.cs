using Windows.UI.StartScreen;

namespace Lumen.Services;

public static class JumpListService
{
    public static async Task TryConfigureAsync()
    {
        try
        {
            var jumpList = await JumpList.LoadCurrentAsync();
            jumpList.Items.Clear();
            jumpList.SystemGroupKind = JumpListSystemGroupKind.None;
            jumpList.Items.Add(JumpListItem.CreateWithArguments("lumen:home", "Home"));
            jumpList.Items.Add(JumpListItem.CreateWithArguments("lumen:search", "Search"));
            jumpList.Items.Add(JumpListItem.CreateWithArguments("lumen:settings", "Settings"));

            try
            {
                var resume = await App.Jellyfin.GetContinueWatchingAsync(6);
                foreach (var item in resume.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
                {
                    var jump = JumpListItem.CreateWithArguments($"lumen:item:{item.Id}", item.Name ?? "Continue watching");
                    jump.GroupName = "Continue Watching";
                    jumpList.Items.Add(jump);
                }
            }
            catch { }

            await jumpList.SaveAsync();
        }
        catch
        {
            // JumpList is optional for unpackaged builds.
        }
    }
}

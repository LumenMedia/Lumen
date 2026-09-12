# Lumen 1.0.0 RC1 test checklist

## Installer

1. Keep your existing `Assets` folder in the project, including `Assets\AutoSync\ggml-tiny.bin`.
2. Open `Lumen.sln`, select **Release / x64**, then rebuild `Lumen.Setup`.
3. Install `Lumen.Setup\Release\Lumen-Setup-1.0.0-RC1.msi`.
4. Confirm the installer identifies itself as **Lumen 1.0.0 RC1** and upgrades the previous RC1 test installation without a manual uninstall.
5. Confirm there is still only one Lumen entry in Windows Settings > Apps.
6. Confirm both Start Menu and Desktop shortcut options are selected by default and both shortcuts launch Lumen with the correct icon.

## Core playback

- Resume a movie and an episode.
- Play from beginning.
- Test 10-second back / 30-second forward.
- Switch audio and subtitle tracks.
- Enter/leave fullscreen and verify controls/cursor hide correctly.
- Check Skip Intro/Credits and the Up Next flow.
- Verify downloaded media is preferred when that setting is enabled.

## Downloads / offline

- Download a title, pause/resume it and complete it.
- Confirm the Item page changes to Downloading / Downloaded.
- Restart Lumen and confirm download history remains.
- Disconnect the Jellyfin server/network and confirm offline Downloads mode opens and local playback works.

## Music

- Start music, browse away while it keeps playing, and use the mini-player.
- Open synced lyrics.
- Run Lyrics AutoSync once to confirm `ggml-tiny.bin` is present in the installed build.

## Final checks

- Search and library navigation.
- Settings category scroll-spy.
- Back navigation.
- No missing Lumen logos/backgrounds/icons.
- Settings > Advanced reports **1.0.0 RC1**.

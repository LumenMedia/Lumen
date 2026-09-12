# Changelog

All notable changes to Lumen will be documented here.

## 1.0.0 RC1 — Build 166

### Release candidate

- Prepared Lumen for its first public release candidate.
- Added a Microsoft Visual Studio Installer Projects MSI with Lumen branding, licence acceptance and optional Start Menu/Desktop shortcuts.
- Both shortcut options are enabled by default and target the installed `Lumen.exe`.
- Added in-place MSI upgrade support for release-candidate testing.
- Added Installed Apps/Add or Remove Programs branding.

### Code quality

- Migrated 207 CommunityToolkit.Mvvm `[ObservableProperty]` backing fields to WinUI/AOT-safe partial properties.
- Preserved existing property names, defaults, bindings and change hooks.
- Made `MVVMTK0045` a build error so the legacy pattern cannot be reintroduced accidentally.
- Cleaned redundant source comments and release packaging metadata.

### Application

- Native WinUI 3 Jellyfin browsing, search and item-detail experience.
- Featured, Continue Watching, Up Next, Recently Released and Recently Added home sections.
- Native video playback with resume, automatic playback negotiation, audio/subtitle selection and intro/credits handling.
- Offline download manager with quality selection, pause/resume/retry and local playback.
- Dedicated Music experience with albums, artists, songs, playlists, genres, favourites and persistent playback.
- Synced lyrics with LRCLIB and local Whisper-based Lyrics AutoSync.
- Full Settings hub, offline startup and downloaded-copy preference.

For pre-public development history, refer to the Git history after the repository's initial import.

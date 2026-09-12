# Contributing to Lumen

Thanks for your interest in contributing to Lumen.

## Before opening an issue

- Check existing issues first.
- For bugs, include the Lumen version, Windows version, Jellyfin server version and clear reproduction steps.
- Remove server addresses, usernames, access tokens and other private information from screenshots or logs.

## Development setup

Lumen is a WinUI 3 / .NET desktop application.

1. Fork and clone the repository.
2. Open `Lumen.sln` in Visual Studio.
3. Use `Release` or `Debug` with the `x64` platform for normal development.
4. Restore NuGet packages.
5. Build and run the `Lumen` project.

The MSI setup project requires the Microsoft Visual Studio Installer Projects extension and is not required for normal application development.

## Pull requests

- Keep changes focused.
- Match the existing Lumen visual language and MVVM structure.
- Avoid introducing web-wrapper UI for features that can be implemented natively.
- Preserve existing playback, download, lyrics and offline behaviour unless the change specifically targets it.
- Test affected flows before submitting.
- Do not commit `bin`, `obj`, installer output, logs or user-specific configuration.

## Observable properties

Lumen uses CommunityToolkit.Mvvm partial observable properties for WinUI/AOT compatibility. Do not reintroduce field-based `[ObservableProperty]` declarations that trigger `MVVMTK0045`.

## Licensing

By contributing, you agree that your contribution may be distributed under the GNU General Public License v3.0 used by this repository.

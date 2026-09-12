# Third-party notices

Lumen is licensed under GPL-3.0. It also depends on or redistributes third-party software and assets with their own licence terms.

This file is an informational inventory for the 1.0.0 RC1 source tree and is not legal advice. Package-specific licence files and terms remain authoritative.

## CommunityToolkit.Mvvm 8.4.0

- Project: .NET Community Toolkit / MVVM Toolkit
- Used for MVVM source generators and observable/command infrastructure.
- Licence: MIT

## Whisper.net 1.9.1 / Whisper.net.Runtime 1.9.1

- Project: Whisper.net
- Used by Lumen's local Lyrics AutoSync feature.
- Licence: MIT
- Native runtime packages include Whisper/whisper.cpp-related binaries; their bundled notices and package terms continue to apply.

## OpenAI Whisper `ggml-tiny.bin`

- Project: OpenAI Whisper
- Used as the local speech-recognition model for Lyrics AutoSync.
- Upstream Whisper repository licence: MIT
- Copyright notice from upstream: Copyright (c) 2022 OpenAI

## Microsoft.WindowsAppSDK 2.4.0

- Project: Windows App SDK / WinUI 3
- Used for Lumen's native Windows application framework and runtime.
- The open-source Windows App SDK repository is MIT licensed, while redistributed NuGet/runtime components may carry Microsoft package-specific software licence terms. Refer to the licence information shipped with the NuGet package/runtime when redistributing binaries.

## Microsoft.Windows.SDK.BuildTools 10.0.28000.2705

- Build-time Windows SDK package from Microsoft.
- Package-specific Microsoft licence terms apply; refer to the NuGet package's Licence Info metadata.

## LRCLIB

Lumen can query the LRCLIB service at runtime for synced lyrics. LRCLIB is not bundled with Lumen. Use of the service is subject to LRCLIB's own terms and policies.

## Jellyfin

Lumen is a third-party client that communicates with a Jellyfin server through Jellyfin APIs. Jellyfin itself is not bundled with this repository. Jellyfin names and marks belong to their respective owners.

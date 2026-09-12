# Lumen assets

This directory contains Lumen's permanent application branding and local runtime assets.

The public repository is expected to contain:

- `Lumen.ico`
- `LumenLogo.png`
- `LoginBackground.png`
- `AutoSync/ggml-tiny.bin`

The image assets are copied into build/publish output. `ggml-tiny.bin` is used by the local Lyrics AutoSync feature and is copied explicitly by the project file.

Do not place C# source files in this directory; `Lumen.csproj` intentionally excludes `Assets/**` from compilation.

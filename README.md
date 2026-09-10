# Camledian Drive

Camledian Drive is an open-source Windows client for connecting to Camledian Commander storage over WebDAV.

Default WebDAV endpoint: `https://admin.camledian.art/webdav/`

## What the first prototype does

- native Windows WPF app on .NET 8
- asks only for the WebDAV username and password
- mounts Commander as `X:` through rclone + WinFsp
- uses rclone's WebDAV backend directly without creating an `rclone.conf`
- sends the plaintext password to `rclone obscure -` through STDIN rather than a process argument
- passes only the obscured password to the long-running mount process
- uses full VFS cache for normal Windows file operations
- opens the mounted drive in File Explorer after a successful mount
- can disconnect the mount from the UI

> This is an early prototype. Persistent credentials, tray mode, reconnect logic,
> installer bundling, dependency verification and code signing are intentionally
> left for the next iterations.

## Requirements for the prototype

1. Windows 11
2. WinFsp installed
3. `rclone.exe` either available in `PATH` or placed in `tools/rclone.exe` next to the published app
4. an enabled Camledian Commander WebDAV account

Camledian Commander currently uses a dedicated WebDAV Basic account rather than the normal browser session. The client is intentionally designed around that boundary so the Commander server remains an independent service.

## Build

```powershell
dotnet build .\src\CamledianDrive\CamledianDrive.csproj -c Release
```

GitHub Actions also builds and publishes a Windows x64 artifact on each push to `main`.

## Architecture

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) and [`docs/ROADMAP.md`](docs/ROADMAP.md).

## License and branding

Source code is MIT licensed. See [`LICENSE`](LICENSE).

The Camledian name, logo and visual identity are not granted under the MIT source-code license. See [`TRADEMARKS.md`](TRADEMARKS.md).

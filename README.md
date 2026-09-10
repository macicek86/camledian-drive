# Camledian Drive

Camledian Drive is an open-source Windows client for connecting to Camledian Commander storage over WebDAV.

Default WebDAV endpoint: `https://admin.camledian.art/webdav/`

## What the first prototype does

- native Windows WPF app on .NET 10
- framework-dependent build for fast development and testing
- asks only for the WebDAV username and password
- mounts Commander as `X:` through the bundled rclone + WinFsp
- GitHub Actions downloads and bundles the current official Windows x64 rclone build
- GitHub Actions also bundles the current official WinFsp MSI installer
- when WinFsp is missing, Camledian Drive offers to install the bundled MSI with UAC elevation
- uses rclone's WebDAV backend directly without creating an `rclone.conf`
- sends the plaintext password to `rclone obscure -` through STDIN rather than a process argument
- passes only the obscured password to the long-running mount process
- uses full VFS cache for normal Windows file operations
- opens the mounted drive in File Explorer after a successful mount
- can disconnect the mount from the UI

> This is an early prototype. Persistent credentials, tray mode, reconnect logic,
> a polished installer, self-contained publishing and code signing are intentionally
> left for the next iterations.

## Requirements for the prototype

1. Windows 11
2. .NET 10 Desktop Runtime installed
3. an enabled Camledian Commander WebDAV account

`rclone.exe` and the WinFsp installer are included in GitHub Actions build artifacts, so testers do not need to download them separately. WinFsp still needs to be installed into Windows because it is the filesystem driver used by rclone mount.

Camledian Commander currently uses a dedicated WebDAV Basic account rather than the normal browser session. The client is intentionally designed around that boundary so the Commander server remains an independent service.

## Build

```powershell
dotnet build .\src\CamledianDrive\CamledianDrive.csproj -c Release
```

GitHub Actions builds and publishes a framework-dependent Windows x64 artifact on each push to `main`, including rclone and the WinFsp installer.

## Architecture

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) and [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Third-party components

The packaged development artifact contains rclone and WinFsp. Their upstream license texts are copied into the corresponding `tools` and `dependencies` directories during the build. These components retain their own upstream licenses and copyrights.

## License and branding

Source code is MIT licensed. See [`LICENSE`](LICENSE).

The Camledian name, logo and visual identity are not granted under the MIT source-code license. See [`TRADEMARKS.md`](TRADEMARKS.md).

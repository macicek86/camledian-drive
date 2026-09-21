# Camledian Drive

Camledian Drive is an open-source Windows client for connecting to Camledian Commander storage over WebDAV.

Default WebDAV endpoint: `https://admin.camledian.art/webdav/`

## What the first prototype does

- native Windows WPF app on .NET 10
- framework-dependent build for fast development and testing
- asks only for the WebDAV username and password
- mounts Commander as `X:` through the bundled rclone + WinFsp
- GitHub Actions builds a pinned rclone version with a small opt-in permission preflight patch (see `vendor/rclone/README.md`)
- GitHub Actions also bundles the current official WinFsp MSI installer
- when WinFsp is missing, Camledian Drive offers to install the bundled MSI with UAC elevation
- GitHub Actions also builds a Windows installer (Inno Setup) that installs WinFsp and, if missing, the .NET Desktop Runtime, so a plain double-click setup is enough
- uses rclone's WebDAV backend directly without creating an `rclone.conf`
- sends the plaintext password to `rclone obscure -` through STDIN rather than a process argument
- passes only the obscured password to the long-running mount process
- uses full VFS cache for normal Windows file operations
- rejects writes into read-only folders before a new file enters the local cache; editor saves in writable folders keep using full caching
- opens the mounted drive in File Explorer after a successful mount
- shows queued uploads and failed transfers in the window and Windows tray notifications
- polls rclone's authenticated loopback control interface, including after app restart
- checks outstanding transfers before disconnecting; interrupting pending or unknown uploads requires explicit confirmation
- can disconnect the mount from the UI

> This is an early prototype. Self-contained publishing and code signing are
> intentionally left for the next iterations.

## Requirements for the prototype

1. Windows 11
2. an enabled Camledian Commander WebDAV account

Using the `CamledianDrive-Setup-win-x64` installer build, the .NET Desktop Runtime and WinFsp are installed automatically if missing. The portable `CamledianDrive-win-x64` build still requires the .NET 10 Desktop Runtime to already be installed; `rclone.exe` and the WinFsp installer are bundled either way, so testers do not need to download them separately.

Camledian Commander currently uses a dedicated WebDAV Basic account rather than the normal browser session. The client is intentionally designed around that boundary so the Commander server remains an independent service.

## Build

```powershell
dotnet build .\src\CamledianDrive\CamledianDrive.csproj -c Release
```

GitHub Actions builds and publishes a framework-dependent Windows x64 artifact on each push to `main`, including rclone and the WinFsp installer, plus an Inno Setup installer built from that same output (see [`installer/CamledianDrive.iss`](installer/CamledianDrive.iss)).

## Architecture

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) and [`docs/ROADMAP.md`](docs/ROADMAP.md).

## Third-party components

The packaged development artifact contains rclone and WinFsp. Their upstream license texts are copied into the corresponding `tools` and `dependencies` directories during the build. These components retain their own upstream licenses and copyrights.

## License and branding

Source code is MIT licensed. See [`LICENSE`](LICENSE).

The Camledian name, logo and visual identity are not granted under the MIT source-code license. See [`TRADEMARKS.md`](TRADEMARKS.md).

## Transfer verification

```sh
dotnet run --project tests/CamledianDrive.TransferTests -c Release
```

The transfer tests run on Linux and Windows without WinFsp. Windows build CI runs
these tests before building the application and installer. Manual Windows QA is
still required; see [transfer monitoring](docs/TRANSFER-MONITORING.md).

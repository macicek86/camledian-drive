# Camledian Drive

Camledian Drive is an open-source Windows client for connecting to Camledian Commander storage over WebDAV.

Target WebDAV endpoint: `https://admin.camledian.art/webdav/`

## Goals

- Native Windows experience
- Mount Camledian storage as a drive in File Explorer
- Simple sign-in for non-technical users
- Secure credential storage
- Automatic reconnect after sign-in, reboot, sleep, or network changes
- Tray status and one-click open/disconnect/reconnect
- Diagnostics without logging passwords or secrets
- Open-source client while the Camledian Commander server may remain closed-source

## Proposed architecture

```text
Camledian Drive (.NET / Windows)
        |
        +-- UI / tray / settings
        +-- Windows Credential Manager or DPAPI
        +-- rclone process management
        +-- WinFsp-backed mount
        |
        +------ HTTPS / WebDAV ------> Camledian Commander
                                      https://admin.camledian.art/webdav/
```

## Repository status

Initial architecture and project skeleton are being prepared.

## Branding

The Camledian name, logo and visual identity are not granted under the source-code license unless explicitly stated otherwise.

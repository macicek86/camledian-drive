# Camledian Drive architecture

## Scope

Camledian Drive is a thin Windows client. It should not duplicate business logic
from Camledian Commander. Authentication, authorization, storage permissions,
quotas, and server-side behavior belong to Commander.

## Components

### Desktop app

Recommended stack: C# / .NET 8 with a native Windows UI. The first implementation
can use WPF for a small dependency footprint and mature Windows integration.

Responsibilities:

- sign-in UX
- connection state
- tray icon
- settings
- reconnect orchestration
- launch / supervise rclone
- open mounted drive in File Explorer
- safe diagnostics

### Credentials

Passwords or access tokens must not be stored in source files, logs, command-line
history, or plain-text configuration. The preferred first implementation is
Windows Credential Manager or DPAPI-protected storage.

### Mount engine

Use rclone for WebDAV protocol handling and VFS behavior. On Windows, rclone mount
uses WinFsp for filesystem integration.

The mount engine should be isolated behind an interface so a future backend can be
swapped without redesigning the UI.

### Server

Default endpoint:

`https://admin.camledian.art/webdav/`

The server remains a separate application and may use a different license.
Communication between the client and server takes place over HTTPS/WebDAV.

## Connection lifecycle

1. User signs in.
2. Client validates connectivity without exposing the password in logs.
3. Client starts the mount engine.
4. Client waits until the drive is ready.
5. UI changes to Connected and Explorer can be opened.
6. On network loss, the app enters Reconnecting state.
7. On sign-out, credentials are removed and the mount is stopped.

## Security requirements

- HTTPS only in production
- TLS certificate validation enabled
- no passwords in process logs
- no passwords in crash reports
- redact authorization headers
- least-privilege server permissions
- optional future token-based login instead of long-lived passwords

## Reliability requirements

The app should recover from:

- Windows sign-in and reboot
- sleep / resume
- Wi-Fi or Ethernet changes
- temporary DNS failures
- temporary server outages
- rclone process exit
- stale mount state

Reconnect should use bounded exponential backoff and surface a useful status to
the user instead of repeatedly showing modal error dialogs.

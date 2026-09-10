# Camledian Commander integration

Camledian Drive is intentionally a thin client for the WebDAV interface exposed by Camledian Commander.

## Endpoint

Production mount URL:

`https://admin.camledian.art/webdav/`

The client treats this as a fixed service endpoint in the first Camledian-branded release.

## Authentication boundary

Commander uses a dedicated WebDAV Basic account rather than the normal browser/admin session cookie. A tenant WebDAV username follows the server-defined `podklady-{tenantId}` format and has its own generated/rotatable password.

Camledian Drive therefore asks for the dedicated WebDAV username and password. It must never attempt to reuse browser cookies or store the password in source code.

## WebDAV capabilities

Commander advertises DAV class 1/2 behavior and handles the methods needed by normal filesystem clients, including OPTIONS, GET, HEAD, PROPFIND, PUT, MKCOL, COPY, MOVE, DELETE, LOCK, UNLOCK and PROPPATCH.

The client uses rclone's generic WebDAV backend (`vendor=other`) so the desktop application does not need to duplicate Commander filesystem semantics.

## Security

- production endpoint is HTTPS only
- certificate validation stays enabled
- plaintext passwords must not be written to logs or config files
- future persistent credentials belong in Windows-protected credential storage
- Commander remains the source of truth for authorization and tenant isolation

## Compatibility goal

The Windows app should recover cleanly from reboot, sign-in, sleep/resume, network changes and temporary server outages without requiring the user to restart the Windows WebClient service.

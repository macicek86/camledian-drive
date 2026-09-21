# Transfer monitoring

`--vfs-cache-mode full` is intentional: normal Windows editors need seekable
read/write files. Explorer's successful copy means the local write completed,
not that Commander accepted it. Do not solve this by blindly disabling caching.

The client starts rclone RC on a random IPv4 loopback port, with a fresh random
password passed only through the process environment. It does not enable the
web GUI, remote file serving, or unauthenticated RC. The RC password and identity
(PID + process start timestamp + port) are stored separately from WebDAV login in
Windows Credential Manager. Matching both PID and start time prevents reuse of a
stale control session after PID reuse. This also lets a restarted app monitor the
already-running mount. Older mounts without RC are reported as unknown, never as
fully uploaded; disconnect/reconnect once to enable monitoring after an upgrade.

The existing three-second foreground/tray timer reads `vfs/queue` followed by
`vfs/stats`. Timer calls never overlap. Pending counts include queued and active
uploads; queue `tries=1, uploading=true` is the first attempt, not a failure.
Queued retries and subsequent attempts produce one notification per current
failure set, plus a persistent window message. Successful retries remove the
warning. Missing/malformed/failed RC responses are unknown, not an empty queue.
The monitor never displays raw RC errors, passwords, or backend logs.

A failed file directly under an order shows a hint to move it into `Interní`.
This is guidance only: Commander still owns all access decisions. The app does
not silently relocate files or delete failed cache entries. For other errors it
reports a failed upload and suggests checking connectivity and target access.
The notification opens the app when clicked. Windows may suppress tray balloons
(e.g. Do Not Disturb); the persistent status remains available in the app.

Both Disconnect and Exit check fresh transfer state before terminating rclone.
If uploads are pending, errored, out of local disk space, or unknown, the default
is to keep the mount alive. A separate explicit Yes can interrupt it; the cache
is retained. This is a snapshot guard, not an atomic write barrier: files still
open in an editor or writes starting after the check are not guaranteed to have
reached the queue. Close editors before disconnecting. Do not describe an empty
queue as proof that all open documents are saved on the server.

## Validation

Automated: run `dotnet run --project tests/CamledianDrive.TransferTests -c Release`.
Checks cover delayed writes, first attempt vs retry, recovery, disk errors,
missing fields, HTTP errors, authentication, cancellation and fresh counters.

Real-engine smoke test: `python tests/rclone-writeback-smoke.py /path/to/rclone`.
It uses rclone's WebDAV server over the same full VFS cache, a disposable backend,
and authenticated RC. It reproduces an initially successful local PUT followed
by a server 403 and a visible queued retry, then checks that a valid upload drains
the queue. No production data or Windows mount is used. CI runs it with the
bundled Windows rclone; Linux validation used rclone v1.75.1.

Manual Windows QA (not replaced by cross-compilation or this smoke test):

1. Connect with the new client, copy a disposable file beside `zakazka.txt`.
   Explorer may initially show success; after the rejected upload, verify the
   app's persistent warning names the file and suggests `Interní`. Verify one
   tray notification, without repeating it on each retry.
2. Move that disposable cached file into `Interní`; verify it can be downloaded
   from the admin and the warning clears once the queue drains.
3. Disconnect while uploading a disposable large file. Choose No: the mount
   stays alive. Repeat and explicitly choose Yes: the app explains pending data
   is local only and preserves the cache.
4. Disable the network, save a disposable file, restore the network: pending /
   failed state must recover after successful upload.
5. Close to tray: monitoring continues. Restart only the GUI while the mount
   remains alive: credentials and PID/start-time matching restore monitoring.
6. Test an existing mount from an older client: unknown state and explicit
   disconnect confirmation, then reconnect to get monitored state.
7. Open an editor, overwrite an existing internal file, save and close it:
   verify the saved file on the server. Full VFS caching remains enabled.

References: https://rclone.org/commands/rclone_mount/#file-caching and
https://rclone.org/rc/#vfs-queue-queue-info-for-a-vfs and
https://rclone.org/rc/#vfs-stats-stats-for-a-vfs

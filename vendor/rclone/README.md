# Camledian rclone write preflight

Upstream: rclone v1.75.1, commit `687d264b689b8c49a67e2e52a8a5e0caa01c04ce`.
License: MIT (upstream COPYING is included in the application bundle).

`v1.75.1-write-guard.patch` adds an opt-in `--webdav-vfs-write-guard` backend
option and a small VFS hook before Create, write Open and destination Rename.
The hook reads the target directory's authenticated `DAV:isreadonly` property
with Depth 0 PROPFIND **before** creating/truncating a cache entry or changing the
local namespace. Unknown, missing, malformed and denied responses fail closed.
Unrelated response hrefs, hosts and non-200 propstats cannot authorize a write.

This uses Commander permissions, not copied hardcoded path rules. The existing
full write cache, seekable editor handles and retry queue stay unchanged. Only
Camledian's launcher enables the option; ordinary rclone behavior is unaffected.
A five-second per-backend, bounded permission cache avoids duplicate network
roundtrips during Create/Open. A new write after expiry needs connectivity; stale
permissions are not accepted if the server cannot be reached. Writes through an
already-open permitted handle still use the cache normally. Server-side checks
remain authoritative: revoked rights during those five seconds, lost network,
quota errors and other later failures still use the existing upload warnings.

This prevents **new** forbidden cached files. It never deletes or silently moves
old failed cached writes. Existing cached files from earlier versions need to be
recovered or moved to an allowed folder by the user. Delete remains usable for
such recovery; the destination check still permits moving out into Interní.

Build: `python scripts/build-rclone.py`. The script verifies the exact upstream
commit, applies the patch without fuzz, runs backend permission tests and builds
the binary. Windows uses the upstream `cmount` tag and cgofuse's non-CGO Windows
implementation to load the installed WinFsp driver. The app explicitly passes
the new flag, so substituting an unpatched binary fails at startup rather than
silently losing the guard.

Updating rclone is now deliberate: update the pinned version and revision,
rebase/review the patch, then run the permission tests, cached-writeback smoke,
guarded VFS smoke and real Windows mount/editor test. Do not revert to downloading
`rclone-current` in release builds. A fork patch adds maintenance work; keep it
small and consider proposing the generic hook upstream separately.

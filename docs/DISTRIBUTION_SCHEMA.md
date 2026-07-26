# Distribution schema

Korean version: [DISTRIBUTION_SCHEMA.ko.md](DISTRIBUTION_SCHEMA.ko.md)

`distribution.json` lists the files the launcher installs into each player's game directory. It is
produced by the manifest tool (`CustomLauncher.ManifestTool`) and read by the launcher on every run.

## Module rules

- Every module is a single file by default. Only modules that need extracting set `packaging` to an
  archive value; extraction is never inferred from a filename.
- All download URLs must be HTTPS. All install paths are relative to the game root.
- Before writing anything, the updater rejects: rooted paths, `..` traversal segments, link entries,
  invalid SHA-256, duplicate ids or paths, dependency cycles, and archive size/entry limits.
- Only paths recorded in the launcher's managed-state index may be replaced or removed. Existing
  unowned files are treated as conflicts, not update targets, so player-installed drop-in content
  survives even if a server manifest is compromised.

## Hosting the files

Any host works as long as it serves each module over **HTTPS with a valid certificate**. The launcher
follows redirects, so a host that hands off to a signed temporary URL is fine. Self-signed
certificates are refused.

**Static web root** — files sit under a path prefix:

    ManifestTool.exe generate ./pack --base-url https://files.example.com/pack

**Seafile shared folder** — share the distribution folder once (a folder link, `/d/<token>/`, not a
per-file `/f/<token>/` link) and every file underneath is reachable through it. The path travels as a
query parameter, so use the template form:

    ManifestTool.exe generate ./pack \
      --url-template "https://seafile.example.com/d/<token>/files/?p=/{path}&dl=1"

One token covers the whole tree; no per-file links are needed. Verified against a live Seafile
instance: the link 302s to `/seafhttp/files/<uuid>/<name>` and returns the file, and non-ASCII path
segments work because the generator percent-encodes each segment.

`{path}` is the module path relative to the scanned folder. If the shared folder and the scanned
folder are not the same level, add the difference to the template (e.g. `?p=/pack/{path}&dl=1`). To
check, paste one generated URL into a browser: it should download, not show an error page.

`distribution.json` is itself one file; a per-file share link is the simplest way to publish it. Put
that URL in `LauncherConfig.ManifestUrl`.

If neither URL option fits, omit both flags: the generator keeps the URL already recorded for each
module, so hand-written links survive regeneration while hashes are refreshed. URLs are stored per
module, so different hosts can be mixed.

## The manifest tool

Run `ManifestTool.exe` with no arguments to open a window (folder picker, URL template, change report
against the previous manifest, remembered fields). Passing arguments runs the command line above,
which stays available for scripting. Both paths call the same generation code.

Operator-facing walkthrough: [OPERATOR_GUIDE.ko.md](OPERATOR_GUIDE.ko.md).

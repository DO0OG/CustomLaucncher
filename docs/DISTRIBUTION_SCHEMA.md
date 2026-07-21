# Distribution schema

## Hosting the files

Any host works as long as it serves each module over HTTPS with a valid certificate. The launcher
follows redirects, so a host that hands off to a signed temporary URL is fine.

**Static web root.** Files sit under a path prefix:

    manifesttool generate ./pack --base-url https://files.example.com/pack

**Seafile shared folder.** Share the distribution folder once - a folder link, `/d/<token>/`, not a
per-file `/f/<token>/` link - and every file underneath is addressable through it. The path travels
as a query parameter, so use the template form:

    manifesttool generate ./pack \
      --url-template "https://seafile.example.com/d/<token>/files/?p=/{path}&dl=1" \
      --existing ./distribution.json --output ./distribution.json

One token covers the whole tree; no per-file links are needed. Verified against a live Seafile
instance: the link 302s to `/seafhttp/files/<uuid>/<name>` and returns the file, and non-ASCII path
segments work because the generator percent-encodes each segment.

`distribution.json` itself is a single file, so a per-file share link is the simplest way to publish
it. Put that URL in `LauncherConfig.ManifestUrl`.

If neither option fits, omit both flags: the generator then keeps the URL already recorded for each
module, so hand-written links survive regeneration while hashes are refreshed.


Modules are single files by default. Bundles must explicitly set `packaging` to
an archive value; extraction is never inferred solely from an untrusted filename.
All URLs must use HTTPS and all paths are relative to the configured game root.
The updater rejects rooted paths, traversal segments, links, invalid SHA-256,
duplicate IDs/paths, dependency cycles, and archive limits before writing.

Only paths recorded in the launcher-owned managed-state index can be replaced or
removed. Existing unowned files are conflicts, not update targets. This preserves
user-installed drop-in content even when a server manifest is compromised.

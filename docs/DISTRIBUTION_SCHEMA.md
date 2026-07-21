# Distribution schema

Modules are single files by default. Bundles must explicitly set `packaging` to
an archive value; extraction is never inferred solely from an untrusted filename.
All URLs must use HTTPS and all paths are relative to the configured game root.
The updater rejects rooted paths, traversal segments, links, invalid SHA-256,
duplicate IDs/paths, dependency cycles, and archive limits before writing.

Only paths recorded in the launcher-owned managed-state index can be replaced or
removed. Existing unowned files are conflicts, not update targets. This preserves
user-installed drop-in content even when a server manifest is compromised.

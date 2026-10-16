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

## Servers

A manifest can describe several servers in a top-level `servers` array. Players pick one in the
launcher. Each entry has these fields:

| Field | Required | Description |
|---|---|---|
| `id` | yes | Lowercase kebab-case matching `^[a-z0-9]+(-[a-z0-9]+)*$`, unique in the manifest. It is the key for the player's selected server and per-server settings. |
| `name` | yes | Name shown in the launcher and the server list. |
| `address` | yes | Server address. |
| `port` | no | 1–65535. Defaults to 25565. |
| `minecraftVersion` | yes | Minecraft version, for example `1.20.1`. |
| `modLoader` | yes | Object with `type` and `version`. `type` is `forge`, `fabric`, `neoforge`, `quilt`, or `vanilla` (lowercase). `version` is required for every type except `vanilla`. The launcher installs `forge`, `fabric`, `neoforge`, and `quilt` when they are not already installed. For `neoforge`, `version` is the NeoForge version (for example `21.1.100`, used with Minecraft `1.21.1`). For `quilt`, `version` is the Quilt Loader version (for example `0.29.1`). |
| `java` | yes | `minVersion`, `recommendedVersion`, and optional `minRamMb`. Same structure as the single-server `java`. |
| `modules` | yes | Module list following the module rules above. |
| `discord` | no | Optional `details`, `state`, `largeImageKey`. When `details` is set, it replaces the server name in the Discord status. `state` is reserved; the launcher does not use it yet. |
| `statusUrl` | no | Absolute `https` URL for the status check. When set, the launcher queries this status API first and uses its result if the request succeeds (no response time is shown). If it is omitted or the request fails, the launcher sends a Minecraft Server List Ping (SLP) directly to `address:port` to read the player count, MOTD, version name, and response time in ms. If a firewall blocks direct connections to the server port, set `statusUrl`. |

Each server has its own game folder. A server whose `id` is `default` (including the old single-server format) uses the configured game folder as-is; every other server uses `{game folder}/instances/{server id}`, so its mods, shader packs, resource packs, options, servers.dat, logs, crash-reports, and saves are kept separately. Minecraft's shared files (libraries, versions, assets), the manifest cache, and the Java runtime are shared. Offline availability is judged per server folder. When the player switches servers and launches, the server-managed mods are synchronized again for the newly selected server. The server selector appears only when
there are two or more servers.

```json
{
  "serverId": "",
  "servers": [
    {
      "id": "main",
      "name": "DOG'S SERVER",
      "address": "play.example.com",
      "minecraftVersion": "1.20.1",
      "modLoader": { "type": "fabric", "version": "0.15.11" },
      "java": { "minVersion": "17", "recommendedVersion": "17", "minRamMb": 4096 },
      "modules": [
        {
          "id": "mods/a",
          "path": "mods/a.jar",
          "url": "https://example.com/pack/mods/a.jar",
          "hash": "9b2f4c1e7a0d35f86b1c9e2a4d7f0b3c6e8a1d5f2b9c4e7a0d3f6b1c8e5a2d4f",
          "type": "RequiredMod",
          "parentId": null,
          "loadOrder": null
        }
      ],
      "discord": { "details": "Playing DOG'S SERVER" }
    },
    {
      "id": "creative",
      "name": "DOG'S CREATIVE",
      "address": "creative.example.com",
      "port": 25566,
      "minecraftVersion": "1.20.1",
      "modLoader": { "type": "forge", "version": "47.3.0" },
      "java": { "minVersion": "17", "recommendedVersion": "17" },
      "modules": [
        {
          "id": "mods/b",
          "path": "mods/b.jar",
          "url": "https://example.com/pack/mods/b.jar",
          "hash": "c41e8a0f5d2b7c93e6a1f4d08b5c2e7a9f3d6b1e4c8a0d2f7b5e9c3a6d1f4b80",
          "type": "OptionalMod",
          "parentId": null,
          "loadOrder": null
        }
      ],
      "statusUrl": "https://status.example.com/creative"
    }
  ]
}
```

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

## Compatibility

- A manifest with no `servers` array, or an empty one, uses the single-server format. The launcher
  reads it as one server with id `default`, built from the launcher's own values (`LauncherConfig`
  name, address, port, Minecraft version, loader) plus the top-level `modules` and `java`.
- Using `servers` and top-level `modules` together is rejected.
- A single invalid server entry (empty or malformed id, duplicate id, unknown loader type, missing
  required field, non-HTTPS `statusUrl`) rejects the whole manifest. Cached copies of old manifests
  follow the same rules.
- New-format manifests set top-level `serverId` to an empty string (the manifest tool writes it that
  way). Older launchers that do not know `servers` reject such a manifest, so they cannot clear the
  mod list by reading it as empty. Operators must deploy the new launcher first, then switch the
  manifest to the new format.
- Player settings: the selected server id (`selectedServerId`) and optional modules disabled per
  server (`disabledOptionalModuleIdsByServer`). The old disabled-module list moves to the `default`
  server on first run. If the saved id is not in the list, the first server is selected.
- Command line: `generate --existing` rejects a manifest that has `servers` (edit it in the tool
  window). `diff` prefixes each change with `[id]` for manifests that have `servers`.

## The manifest tool

Run `ManifestTool.exe` with no arguments to open a window (folder picker, URL template, change report
against the previous manifest, remembered fields). Passing arguments runs the command line above,
which stays available for scripting. Both paths call the same generation code.

Operator-facing walkthrough: [OPERATOR_GUIDE.ko.md](OPERATOR_GUIDE.ko.md).

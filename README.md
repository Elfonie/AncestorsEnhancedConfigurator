# Ancestors Enhanced Configurator

Ancestors Enhanced Configurator (AEC) is a portable desktop application for **Ancestors: The Humankind Odyssey**. It provides verified graphics editing, local save checkpoints, graphics profiles, local hardware guidance, diagnostics, and an exact-build gameplay-difficulty system.

AEC is deliberately fail-closed. Detecting a game folder is not enough to authorize a write: the application verifies the store/build or stock-content signature, the current installation context, and every target again before committing a supported change.

Release packages are self-contained folder builds. No installer or separate .NET runtime is required.

## Current scope

- Home dashboard with installation status and a locally derived graphics starting point
- Simple and Advanced graphics views with search and All/Modified/Game-default filters
- Seven combinable built-in graphics tweaks: Clear Image, Performance, Balanced, High Quality, Ultra, Low VRAM, and Cinematic
- Editable display, frame-rate, brightness, quality, UE4 override, vignette, and startup-video settings when their targets are verified
- Graphics and display `.aecprofile` files with save, import, export, compare, duplicate, rename, and delete actions
- Manual and automatic checkpoints for all five local save slots
- Checkpoint search, origin filters, names, notes, favorites, restore confirmation, and deletion
- Exact-build gameplay PAK creation, update, detection, review, and removal
- Persisted high contrast, optional Discord Rich Presence, experimental graphics/gameplay toggles, onboarding state, and detailed hardware results
- Read-only diagnostics for the installation, configuration files, PAK classification, hardware, and inspection notices
- Self-contained Windows x64 and Linux x64 builds

AEC no longer contains the old save-value cheats. It does not edit neuronal energy, current health, needs, or clan members inside a live save.

## Quick start

1. Start Ancestors once and create a save so the game creates its local files.
2. Close the game before changing graphics, `System.sav`, PAKs, or restoring a checkpoint.
3. Extract the complete AEC archive and start the application.
4. Choose settings or a tweak, select **Review**, inspect the exact values and files, and confirm the operation.

Manual and automatic checkpoints may be created while Ancestors is running. Restoring a checkpoint and changing configuration or PAK files require the game to be closed.

## Compatibility

| Installation | Detection | Graphics and checkpoints | Gameplay difficulty |
| --- | --- | --- | --- |
| Steam on Windows | Supported | Supported when build/content identity is verified | Available only for exact Steam build `5495393` with the researched stock signature; a controlled run confirms the base regimen overlay reaches live saved state, while the complete control/effect matrix remains pending |
| Epic Games on Windows | Supported | Supported when the stock-content signature is verified | Not supported |
| GOG on Windows | Supported | Supported when the stock-content signature is verified | Not supported |
| Steam through Proton on Linux | Supported | Supported when the Steam installation and Proton user-data path are unambiguous and verified | Same exact-build restriction; no Linux runtime pass has confirmed load priority and player-visible effects |
| Heroic | Detection only | Read-only | Not supported |

AEC checks Steam, Epic Games, GOG, and Heroic in a deterministic order. If several distinct installations or several possible Proton/Wine users are found, AEC refuses to choose a write target automatically.

## Home and hardware guidance

The Home page reads local CPU, memory, and GPU inventory and can stage a conservative graphics tweak. A recommendation is a starting point, not a benchmark or an FPS guarantee, and it always enters the normal review flow before writing anything.

On Windows, the normal WMI GPU-memory value is informational and is not trusted for recommendations. **Check GPU details** is an explicit, persisted opt-in that runs a bounded DxDiag scan and accepts only dedicated memory as authoritative. On Linux, AEC reads CPU/memory information from `/proc` and GPU information from `/sys/class/drm`; a recommendation is available only when authoritative VRAM is exposed by the driver.

## Graphics

**Simple** contains the controls with the clearest player-visible effect. **Advanced** contains all verified controls, inspection details, search, and additional technical values. Rare or low-confidence renderer controls remain hidden until **Enable experimental graphics settings** is enabled in Settings; they appear only in Advanced.

Built-in graphics entries are partial tweaks, not mutually exclusive game-wide quality presets. They affect only their listed settings and may be combined. The resulting union is shown in Review.

Custom overrides replace only selected parts of the current in-game quality preset. Values still controlled by the game display their effective Low/Medium/High preset value where known.

Available safety actions include:

- **Use game graphics settings** removes the custom graphics values currently managed in the editor.
- **Undo last apply** reverts the newest still-provable AEC settings operation.
- **Remove Configurator changes** restores verified tool-owned files to their recorded pre-AEC baseline.

## Configuration profiles

Profiles store the current editable graphics and display setup in a bounded `.aecprofile` JSON document. Loading a profile resets omitted managed overrides to their game-controlled value, so Compare includes both applied profile values and overrides that will return to Game default.

Profiles can be imported, exported, compared, duplicated, renamed, and deleted with confirmation. Invalid or unreadable local profiles are left untouched and reported to the user. Profile files support both graphics and display settings; they are loaded through the same validated editor pipeline. Gameplay profile sections remain reserved and are rejected by schema validation.

## Save checkpoints

AEC validates the compressed save before publishing a checkpoint. A checkpoint is first built and re-read in a temporary directory, then published atomically. Restoring a checkpoint creates a **PreRestore** safety checkpoint of the current live save before the compare-and-swap replacement.

The normal retention target is 50 checkpoints per slot. Favorites are pinned and are not automatically deleted, so a slot may temporarily contain more than 50 checkpoints when preserving favorites requires it. If favorite metadata cannot be read safely, retention keeps the affected checkpoints instead of guessing.

Auto-Backup watches the five canonical save slots and uses a configurable minimum interval. Pending saves receive a final no-cooldown backup attempt when the watcher stops. If Auto-Backup and **Keep auto-backup running when I close the window** are enabled, closing the window hides AEC in the system tray; choose **Exit** from the tray to stop it.

Backup Health reports what AEC could parse during its latest scan. It does not prove that a checkpoint has been restored successfully in-game.

## Gameplay difficulty

Gameplay difficulty is build-bound to Steam build `5495393` and the matching researched stock PAK signature. Standard controls use 10% steps from 10% through 200%. The explicit experimental toggle extends all percentage controls to 1000%; this is an extreme technical range, not a balanced preset range.

Simple contains six coordinated survival and hazard controls. The requirement controls change
how much recovery is needed; they do not shorten the normal meter cycle, which is driven by
the game's 1,440 virtual-minute day:

| Control | Game default | Direction |
| --- | --- | --- |
| Food required | 24 portions per day | Higher is harder because each portion restores less |
| Water required | 30 portions per day | Higher is harder because each portion restores less |
| Sleep required | 16 portions per day | Higher is harder because each portion restores less |
| Fall damage | Minor 2.5%, Major 5% | Higher is harder |
| Bleeding health loss | Minor 1%, Major 2% | Higher is harder |
| Poison health loss | Minor 1%, Major 2% | Higher is harder |

Advanced adds nine independently editable controls:

- Energy recovery speed
- Wound healing from sleep
- Wound maximum-stamina penalty
- Poison recovery from liquid/sleep portions
- Rest delay after energy use
- Exhaustion threshold
- Exhaustion stamina penalty
- Major wound recovery duration
- Major poison maximum-stamina penalty

Researched values that lack a verified asset-backed edit target or a proven consumer (minor wound base recovery time, minor poison stamina penalty, and stamina regained on a consumed portion) are excluded from the configurator.

Game default, Explorer, Survival, Hardcore, and Custom provide starting presets. Presets configure the six Simple controls and leave Advanced controls at game default (subject to the runtime verification gate below).

AEC verifies stock asset SHA-256 values and expected bytes, creates a deterministic PAK v5, reads it back, and installs it together with a content-bound ownership marker through the backed-up transaction system. After restart, AEC accepts an installed gameplay PAK only when the marker, package hash, asset definitions, and encoded percentages agree. Game default removes only an exactly verified AEC-owned package. An external or unverified same-name PAK blocks editing.

### Gameplay runtime gate

Static research, deterministic PAK construction, readback, ownership, and transaction safety are covered. A controlled Windows save observation also confirms that the base regimen overlay reaches live saved state. That observation does **not** prove that every percentage, asset, or hazard control produces the expected player-visible behavior.

Before treating Gameplay as runtime-verified for a release, the exact supported game build still needs an in-game pass covering all enabled controls, PAK priority, restart detection, stock restoration, and save-game safety. In particular, `NeededPerDay` must not be described as changing passive hunger, thirst, or sleep drain speed: the verified property changes required portions and per-portion effect, while the normal day cycle remains fixed. The application reports this as **runtime verification pending**.

## Transaction and ownership safety

Supported writes use one coordinated mutation path with revalidation, compare-and-swap replacement, before-image backups, recovery journals, and post-write verification. Recovery manifests are committed only after required backups are safely written. Interrupted operations are recovered only from states whose hashes AEC can prove; foreign content is preserved for manual action.

PAK ownership is content-based. A filename beginning with `AncestorsEnhanced` is not sufficient proof. Gameplay packages require a matching ownership record, while the vignette package is accepted only when its complete deterministic content can be reconstructed and verified.

**Remove Configurator changes** does not remove the application, game-progress saves, checkpoints, cloud data, external mods, or files changed manually/through another tool. A pre-AEC baseline cannot be reconstructed for modifications made before baseline tracking existed.

## Cloud saves

Restoring a local checkpoint can cause a Steam/Epic/GOG cloud conflict. Compare timestamps and sizes before choosing a version. The local file is the intended version only after a deliberate local restore. Keep an independent copy of important saves before release testing.

## Settings, accessibility, and diagnostics

- High contrast changes AEC only and adds stronger shared-surface borders and keyboard focus.
- Discord Rich Presence is optional and off by default. The local Discord SDK starts only when enabled.
- Experimental graphics exposes rarely useful renderer controls in Graphics > Advanced.
- Experimental gameplay extends the percentage range from 200% to 1000%.
- Diagnostics is read-only and can copy a support report with personal user paths and Steam IDs redacted.
- The first-instance listener activates the existing window when AEC is started a second time.

## Release archives

The build workflow produces these self-contained folder archives:

| Platform | Archive | Start |
| --- | --- | --- |
| Windows x64 | `AncestorsEnhanced-1.0.0-win-x64.zip` | Extract everything and run `AncestorsEnhanced.App.exe` |
| Linux x64 | `AncestorsEnhanced-1.0.0-linux-x64.zip` | Extract everything and run `./AncestorsEnhanced.App` |

On Linux, run `chmod +x AncestorsEnhanced.App` if the archive tool did not preserve the executable bit. Keep all extracted libraries and native files beside the executable.

CI smoke-tests `--version` on native Windows and Linux runners, verifies the unpacked archives, and publishes an archive SHA-256 file. Every archive also contains this README, the MIT license, third-party notices, and `SHA256SUMS.txt` with per-file hashes.

Published releases, when available, are listed on [GitHub Releases](https://github.com/Elfonie/AncestorsEnhancedConfigurator/releases) and the project's Nexus Mods page.

## Antivirus reports

Release folders are not single-file bundled, compressed executables, and debug symbols are excluded. Antivirus products can therefore inspect each managed and native file separately. Generic heuristic detections may still occur because AEC modifies game configuration/PAK files, performs guarded byte-level asset patches, and ships the optional Discord native SDK. Windows releases are currently not Authenticode-signed.

Verify the archive against its `.zip.sha256` file and the extracted files against `SHA256SUMS.txt`. If a scanner reports a detection, include the product, engine version, and exact signature in the issue; do not disable security software globally.

## Logs and local data

The diagnostics log is stored at:

- Windows: `%LocalAppData%\AncestorsEnhanced\Logs\AncestorsEnhanced.log`
- Linux: the platform local-application-data directory, normally `~/.local/share/AncestorsEnhanced/Logs/AncestorsEnhanced.log`

Preferences and profiles are stored below the platform's local application-data directory. Auto-backup settings and checkpoint metadata are stored in `AncestorsEnhanced_ToolSettings.json` inside the detected Ancestors user-data directory. Check diagnostic output for personal paths before sharing it, and never attach a complete save file to a public issue.

## Build from source

The repository targets .NET 10 and pins SDK `10.0.302` in [`global.json`](global.json). Package lock files are committed and CI restores them in locked mode.

```text
dotnet restore AncestorsEnhanced.slnx
dotnet build AncestorsEnhanced.slnx -c Release --no-restore
dotnet test AncestorsEnhanced.slnx -c Release --no-build --no-restore
dotnet publish src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj -p:PublishProfile=win-x64
dotnet publish src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj -p:PublishProfile=linux-x64
```

CI runs Release build/tests and a transitive NuGet vulnerability gate on native Windows and Linux runners before publishing self-contained folder artifacts.

## Project status

The current automated suite covers Core, Infrastructure, and Avalonia application behavior, including identity checks, transaction/recovery rules, PAK construction, checkpoint safety, profile validation, hardware parsing, accessibility, focus handling, and single-instance activation.

Automated and synthetic tests do not replace testing with a real Ancestors installation, Proton, cloud synchronization, or an actual in-game restore/gameplay run. Claims requiring that evidence remain explicitly pending.

Ancestors Enhanced Configurator is an unofficial community project and is not affiliated with Panache Digital Games or Private Division.

Released under the [MIT License](LICENSE).

# Ancestors Enhanced Configurator

A portable desktop configurator for **Ancestors: The Humankind Odyssey**. It exposes verified graphics options, manages local save checkpoints, and provides a small set of experimental save modifications.

Release packages are self-contained. Extract the archive and start the application; no installer or separate .NET runtime is required.

## Highlights

- Simple and advanced graphics controls, including supported UE4 overrides
- Editable `System.sav` display, frame-rate, brightness, and quality settings
- Vignette and startup-video controls
- Review before write, verified backups, Undo, and removal of tool-owned changes
- Manual and automatic checkpoints for all five local save slots
- Portable Windows and Linux builds
- Fail-closed editing when the installed game or target file cannot be verified

## Quick start

1. Start Ancestors once so it creates its local files.
2. Close the game, extract the Configurator archive, and start the application.
3. Adjust the settings, select **Review**, and confirm the changes.

Creating normal save checkpoints and automatic checkpoints is allowed while the game is running. Restoring a checkpoint or creating or restoring an experimental save modification requires the game to be closed.

## Compatibility

| Installation | Detection | Graphics editing | Save checkpoints | Experimental save modifications |
| --- | --- | --- | --- | --- |
| Steam on Windows | Supported | Supported for the verified build | Supported | Available, experimental |
| Epic Games on Windows | Supported | Supported with a verified content signature | Supported with a verified installation | Available, experimental |
| GOG on Windows | Supported | Supported with a verified content signature | Supported with a verified installation | Available, experimental |
| Steam through Proton on Linux | Supported | Supported for a verified installation | Supported | Available, experimental |
| Heroic | Detection only | Read-only in 0.9.0 | Read-only in 0.9.0 | Unavailable in 0.9.0 |

Detection alone never authorises a write. Before changing a file, the Configurator verifies the game identity, installation context, and target. Missing or contradictory evidence disables editing instead of guessing.

## Graphics settings

The Graphics page contains two views:

- **Simple** shows the settings with the clearest visual effect.
- **Advanced** shows the deeper verified settings and inspection details.

Custom settings replace only the selected parts of the game's current preset. The review screen lists the exact files and values that will change before anything is written.

Available safety actions include:

- **Undo** reverts the most recent Configurator operation.
- **Clear my custom overrides** reviews and removes the custom graphics values currently shown in the editor.
- **Remove tool changes** restores verified tool-owned settings files to their recorded pre-tool state.

## Save checkpoints

The Configurator can create and restore checkpoints for each of the five Ancestors save slots. It retains up to 50 checkpoints per slot. Optional automatic checkpoints are created in the background when the game saves, subject to the selected minimum interval.

Restoring a checkpoint first creates a safety checkpoint of the current live save. A restore still changes live game progress, so verify the result in-game before continuing a long play session.

## Experimental save modifications

The following modifications create a separate checkpoint instead of overwriting the live save:

- Max Neuronal Energy
- Max Stamina and Energy
- Heal Current Ape

The injector uses explicit structural paths, rejects ambiguous matches, and verifies its own output. Real-save and in-game validation is not yet complete for every modification, so this part of 0.9.0 remains experimental. Keep your own save backup and verify the result in-game.

Developer validation details are tracked in [the save fixture documentation](tests/AncestorsEnhanced.Infrastructure.Tests/SaveGames/Fixtures/README.md).

## Removing tool changes

From the first settings write, the Configurator records a private baseline for every file it owns. **Remove tool changes** is available only when that baseline exists and the current installation and files still match a state the tool can prove.

It restores settings, `System.sav`, vignette, and startup-video changes made through the settings editor. It does not remove:

- the Configurator itself
- save checkpoints or game-progress saves
- Steam Cloud data
- external mods
- edits owned by another tool or made manually

The operation is reversible through Undo. A baseline cannot be reconstructed for files modified before the baseline feature existed.

## Steam Cloud

Steam Cloud may report a conflict after restoring a checkpoint or applying an experimental modification. Do not select a conflict option automatically. Compare the dates and sizes first. The local file is the intended version only after a deliberate local restore.

Keeping a separate copy of the local save directory before an important restore is recommended.

## Download and run

Download the matching archive from [GitHub Releases](https://github.com/Elfonie/AncestorsEnhancedConfigurator/releases) or the Ancestors Enhanced Configurator page on Nexus Mods.

| Platform | Archive | Start |
| --- | --- | --- |
| Windows x64 | `AncestorsEnhanced-0.9.0-win-x64.zip` | Extract and run `AncestorsEnhanced.App.exe` |
| Linux x64 | `AncestorsEnhanced-0.9.0-linux-x64.zip` | Extract and run `./AncestorsEnhanced.App` |

On Linux, run `chmod +x AncestorsEnhanced.App` if the archive tool did not preserve the executable bit.

Each archive contains the application, this README, the MIT license, and `SHA256SUMS.txt`. A separate `.zip.sha256` file is supplied for verifying the complete download.

## Logs and bug reports

The local log is stored at:

- Windows: `%LocalAppData%\AncestorsEnhanced\Logs\AncestorsEnhanced.log`
- Linux: the platform-specific local application-data directory under `AncestorsEnhanced/Logs/`

When reporting a problem, include the operating system, game store, detected build information, the action that failed, and the relevant log section. Check logs for personal paths before sharing them. Never attach a complete save file to a public issue.

## Build from source

The required SDK version is defined in [`global.json`](global.json).

```text
dotnet restore AncestorsEnhanced.slnx
dotnet build AncestorsEnhanced.slnx -c Release --no-restore
dotnet test AncestorsEnhanced.slnx -c Release --no-build
dotnet publish src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj -p:PublishProfile=win-x64
dotnet publish src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj -p:PublishProfile=linux-x64
```

The publish profiles create self-contained single-file builds. GitHub Actions builds Windows and Linux on their native runners, checks dependencies for known vulnerabilities, smoke-tests both executables, verifies the packaged archives, and produces SHA-256 checksums.

## Project status

Automated tests verify code paths and file-safety rules. They do not replace release testing with a real Ancestors installation, a Proton environment, Steam Cloud, or real in-game save modifications.

Ancestors Enhanced Configurator is an unofficial community project and is not affiliated with Panache Digital Games or Private Division.

Released under the [MIT License](LICENSE).

# Ancestors Enhanced Configurator

A portable desktop configurator for **Ancestors: The Humankind Odyssey**. It can adjust verified graphics settings, manage local save checkpoints, and provide a small set of optional quality-of-life tweaks. No installation or separate .NET runtime is required for the release packages.

> **Release status:** `0.9.0` is the upcoming release. Until its release package is published, use the source code only if you are comfortable building it yourself.

## What it does

- **Graphics**
  - Simple and advanced graphics controls
  - System.sav controls for resolution, brightness, frame-rate limit, and quality categories
  - Vignette, startup-video, and supported UE4 renderer overrides
  - Review before write, backups, Undo, and Restore game defaults
  - Remove tool changes returns only unchanged files managed by the Configurator to their captured pre-change state
- **Save checkpoints**
  - Create and restore checkpoints for the five Ancestors save slots
  - Up to 50 retained checkpoints per slot
  - Optional automatic checkpoints when the game saves
- **Optional tweaks**
  - Experimental: Max Neuronal Energy, Max Needs, and Heal Current Ape create a separate checkpoint first; keep your own save backup and verify the result in-game

## Compatibility

The tool searches for installations from Steam, Epic Games, GOG, and Heroic. Steam on Windows and Steam through Proton on Linux are supported detection paths; Epic and GOG are supported on Windows.

Finding an installation is not the same as authorising an edit. Before a setting is written, the tool verifies the game build, installation context, and target file. If that verification is incomplete or contradictory, editing is disabled instead of guessing. Heroic detection is included, but should be treated as a compatibility path that still needs real-world testing.

## Before you use it

1. Start Ancestors once so it creates its local data.
2. Close the game before creating, restoring, or modifying saves.
3. Start the Configurator and let it scan the installation.
4. Review every pending change before confirming it.
5. After restoring a checkpoint, start the game and verify the result before continuing a long play session.

The Configurator keeps its own backups, but they are not a substitute for keeping a copy of your save directory before important changes.

## Remove tool changes

From the first configuration write, the Configurator records a private baseline for each file it changes. **Remove tool changes** is available only when that baseline exists and the current installation and file contents still match the tool-managed state. It restores graphics, System.sav, vignette, and startup-video changes that were made through the settings editor, then marks those files as restored to their baseline state.

It never removes the app, save checkpoints, game-progress saves, Steam Cloud data, external mods, or edits made outside the Configurator. The private baseline remains available internally so undoing the removal is reversible; while no tool changes are active, the removal button stays unavailable. For installs that were already modified before this feature existed, no original baseline can be reconstructed, so the button stays unavailable.

## Savegame-cheat status

The savegame injector is designed to fail closed: it uses explicit structural paths, rejects ambiguous matches, verifies its own output, and creates a checkpoint rather than overwriting the live save.

It is nevertheless still awaiting validation with anonymised real save files and an in-game verification run for each cheat. Treat savegame cheats in 0.9.0 as experimental and keep your own backup. Details are in [the validation gate](tests/AncestorsEnhanced.Infrastructure.Tests/SaveGames/Fixtures/README.md).

## Steam Cloud

Steam Cloud can report a conflict after a checkpoint restore or a savegame cheat. Do not choose a conflict option automatically: compare the dates and sizes first. The local version is the intended one only if you deliberately restored or applied that checkpoint. Keeping a manual copy of the local save folder before resolving a conflict is recommended.

## Download and run

When 0.9.0 is published, download the matching archive from [GitHub Releases](https://github.com/Elfonie/AncestorsEnhancedConfigurator/releases).

| Platform | Archive | Start |
| --- | --- | --- |
| Windows x64 | `AncestorsEnhanced-0.9.0-win-x64.zip` | Extract, then run `AncestorsEnhanced.App.exe` |
| Linux x64 | `AncestorsEnhanced-0.9.0-linux-x64.zip` | Extract, then run `./AncestorsEnhanced.App` |

On Linux, run `chmod +x AncestorsEnhanced.App` first if your archive tool did not preserve the executable bit.

## Logs and troubleshooting

The tool writes a local log file to:

- Windows: `%LocalAppData%\AncestorsEnhanced\Logs\AncestorsEnhanced.log`
- Linux: the platform-specific local application-data directory under `AncestorsEnhanced/Logs/`

The log records detection and operation errors. Do not share a full save file in a bug report. If a report needs a log, check it first for paths or other personal information.

## Build from source

Requirements: [.NET SDK 10.0.302](global.json).

```text
dotnet restore AncestorsEnhanced.slnx
dotnet build AncestorsEnhanced.slnx -c Release --no-restore
dotnet test AncestorsEnhanced.slnx -c Release --no-build
dotnet publish src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj -p:PublishProfile=win-x64
dotnet publish src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj -p:PublishProfile=linux-x64
```

The publish profiles create self-contained, single-file builds in `src/AncestorsEnhanced.App/bin/Release/net10.0/{win-x64,linux-x64}/publish/`. Create the release archives and checksums from those exact outputs.

## Project status

Automated builds and tests run on Windows and Ubuntu. They verify code paths and file safety rules; they do not replace testing the released application with a real Ancestors installation, Proton/Heroic setup, or real savegame cheats.

Ancestors Enhanced Configurator is an unofficial community project. It is not affiliated with Panache Digital Games or Private Division.

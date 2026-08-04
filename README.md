# Ancestors Enhanced Configurator

Portable configurator for **Ancestors: The Humankind Odyssey**. Edit graphics settings, manage save checkpoints and apply safe cheats — all from a small desktop tool. No installation required.

## Download

Release packages are **self-contained** — you do **not** need .NET or any framework installed.

* **Windows**: download `AncestorsEnhanced-0.8.0-win-x64.zip`, extract it, double-click `AncestorsEnhanced.App.exe`.
* **Linux**: download `AncestorsEnhanced-0.8.0-linux-x64.zip`, extract it, and run `./AncestorsEnhanced.App`.

## Quick start

1. Start Ancestors at least once so the game creates its save data.
2. Close Ancestors, then start the Configurator.
3. The tool auto-detects your installation (Steam, Epic, GOG or Heroic) and user data.
4. Adjust graphics settings, manage saves, or apply cheats.

## Features

* Game detection: **Steam** (Windows + Linux/Proton), **Epic** (Windows), **GOG** (Windows), **Heroic Games Launcher** (Epic + GOG, Windows + Linux).
* Simple and advanced graphics controls with "Review before write" and exact backups / undo.
* System.sav controls: resolution, brightness, frame-rate limit and six quality categories.
* Startup video control and configurable vignette strength (safe PAK patch, game files never changed).
* Save Manager: keep and load old save states per slot (5 slots, 50 checkpoints each).
* Auto-Backup Watchdog: creates background checkpoints whenever the game saves.
* Cheats tab: Max Neuronal Energy, Max Needs, Heal Clan — each saved as a new checkpoint, never overwriting your live save.

## Troubleshooting helpers

* A log file is written to `<local-app-data>\AncestorsEnhanced\Logs\AncestorsEnhanced.log` (Windows) or the equivalent local-app-data path (Linux). It records the detected store, whether user data was found, and any crash details. Attach it if you report an issue.

## Steam Cloud note

Ancestors uses Steam Cloud saves. After restoring a checkpoint or applying a cheat, Steam may show a "Cloud Conflict" on next launch. Choose **"Upload to Steam Cloud (Local files)"** to keep the current local save. For reliable restores, consider pausing Steam Cloud for Ancestors first.

## Release building

Requires the .NET 10 SDK.

```text
dotnet build AncestorsEnhanced.slnx
dotnet test AncestorsEnhanced.slnx
dotnet publish src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj -p:PublishProfile=win-x64
dotnet publish src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj -p:PublishProfile=linux-x64
```

Single-file outputs are produced under `src/AncestorsEnhanced.App/bin/Release/net10.0/{win-x64,linux-x64}/publish/`. Ship only the executable in the archive.

Unofficial project, not affiliated with Panache Digital Games or Private Division.
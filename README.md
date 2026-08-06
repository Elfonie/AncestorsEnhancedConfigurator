# Ancestors Enhanced Configurator

Portable configurator for **Ancestors: The Humankind Odyssey**. Edit graphics settings, manage save checkpoints and apply safe cheats — all from a small desktop tool. No installation required.

## Download

Release packages are **self-contained** — you do **not** need .NET or any framework installed.

* **Windows**: download `AncestorsEnhanced-0.9.0-win-x64.zip`, extract it, double-click `AncestorsEnhanced.App.exe`.
* **Linux**: download `AncestorsEnhanced-0.9.0-linux-x64.zip`, extract it, and run `./AncestorsEnhanced.App`.

## Quick start

1. Start Ancestors at least once so the game creates its save data.
2. Close Ancestors, then start the Configurator.
3. The tool auto-detects your installation (Steam, Epic, GOG or Heroic) and user data.
4. Use the **Graphics** tab to adjust settings, **Saves** to manage checkpoints, or **Cheats** to boost yourself.
5. Review your changes before they are written — the tool never edits files without your confirmation.

## Features

* Game detection: **Steam** (Windows + Linux/Proton), **Epic** (Windows), **GOG** (Windows), **Heroic Games Launcher** (Epic + GOG, Windows + Linux).
* **Graphics** — Simple and advanced controls with review-before-write, exact backups and undo.
* **Save manager** — keep and load old save states per slot (5 slots, up to 50 checkpoints each).
* **Auto-backup** — automatically saves a checkpoint whenever the game saves.
* **Cheats** — Max Neuronal Energy, Max Needs, Heal Current Ape. Each is saved as a new checkpoint, your live save is never touched.
* **System.sav controls** — resolution, brightness, frame-rate limit and six quality categories.
* **Startup & camera** — skip startup videos and enable a free camera (F10 in-game).

### What each cheat does

* **Max Neuronal Energy** — sets your neuronal energy to maximum, filling every neuronal energy source.
* **Max Needs** — fills the current ape's needs (stamina, energy and regimen).
* **Heal Current Ape** — restores health, stamina and energy for your currently controlled ape.

## Troubleshooting helpers

A log file is written to `<local-app-data>\AncestorsEnhanced\Logs\AncestorsEnhanced.log` (Windows) or the equivalent local-app-data path (Linux). It records the detected store, whether user data was found, and any crash details. Attach it if you report an issue.

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
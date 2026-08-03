# Ancestors Enhanced Configurator

Portable graphics configurator for Ancestors The Humankind Odyssey

## Features

* Steam detection on Windows
* Steam and Proton detection on Linux
* Epic Games detection on Windows
* GOG detection on Windows
* Simple and advanced graphics controls
* Review before every write
* Exact backups and undo
* System.sav graphics controls
* Startup video control
* Vignette strength from 0 to 100 percent
* Save Manager: keep and load old save states per slot
* Auto-Backup Watchdog: checks into the background whenever the game saves

The Vignette control reads the verified original asset from the installed game and creates a separate PAK patch. Original game PAK files are never changed. Unknown and conflicting vignette patches disable this control.

Editing is enabled only when the executable and a known Steam build or known game content fingerprint match.

System.sav controls include resolutions brightness frame-rate limit and the six built-in quality categories. The base preset and custom-state flag are read automatically.

The Save Manager keeps a history of your Savegame*.sav files per slot (0-4). Each slot keeps its own checkpoints (50 by default; the oldest are removed when full). Loading a checkpoint writes it back to the game save and first stores a safety backup of the current state. Steam Cloud can overwrite a loaded save on the next start, so pause or disable Steam Cloud before restoring.

## Development

Requires .NET 10 SDK

```text
dotnet build AncestorsEnhanced.slnx
dotnet test AncestorsEnhanced.slnx
dotnet run --project src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj
```

Unofficial project not affiliated with Panache Digital Games or Private Division

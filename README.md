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

The Vignette control reads the verified original asset from the installed game and creates a separate PAK patch. Original game PAK files are never changed. Unknown and conflicting vignette patches disable this control.

Editing is enabled only when the executable and a known Steam build or known game content fingerprint match.

System.sav controls include resolutions brightness frame-rate limit and the six built-in quality categories. The base preset and custom-state flag are read automatically. HDR XeSS DirectX 12 and new game assets are not added.

## Development

Requires .NET 10 SDK

```text
dotnet build AncestorsEnhanced.slnx
dotnet test AncestorsEnhanced.slnx
dotnet run --project src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj
```

Unofficial project not affiliated with Panache Digital Games or Private Division

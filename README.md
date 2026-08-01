# Ancestors Enhanced Configurator

An unofficial, portable configurator for *Ancestors: The Humankind Odyssey*.

Version 0.2 detects a native Windows Steam installation and displays its configuration state. It reads Steam metadata, INI settings, and PAK file metadata. Verified overrides are translated into understandable setting cards; raw details remain available in a collapsed technical section. Game-file writes remain disabled.

## Project principles

- Portable application: no service, startup entry, registry installation, or background updater.
- Read-only before write support.
- Every future change must support preview, validation, backup, conflict detection, and rollback.
- Windows and Linux remain separate hosts with shared core logic.
- Store and game-build compatibility are detected, never assumed.
- No telemetry or automatic network access in the application.

## Local development

Requirements:

- .NET 10 SDK
- Git

Build and test:

```text
dotnet build AncestorsEnhanced.slnx
dotnet test AncestorsEnhanced.slnx
```

Run the desktop application:

```text
dotnet run --project src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj
```

## Status

The current build can:

- locate Steam through read-only registry sources and the default Windows path;
- follow every library listed in Steam's `libraryfolders.vdf`;
- validate Steam App ID 536270, the install directory, executable, and build ID;
- locate the Ancestors user-data directory;
- parse all top-level INI files without discarding duplicate keys;
- translate verified overrides such as motion blur, depth of field, texture filtering, sharpening, TAA response, view distance, texture memory, light shafts, and startup-video skipping into readable values;
- identify `SaveGames/System.sav` as the separate binary source used for built-in game settings;
- list PAK names, sizes, timestamps, conservative classifications, and fingerprints for small patch packages;
- recognize the project's known half-strength vignette patch by its exact fingerprint;
- refresh the complete snapshot without changing any source file.

This is development software and not yet a mod release. It must not be presented as an official Panache Digital Games or Private Division product.

Version 0.2 does not claim to decode values stored inside `System.sav`. Local inspection proves that graphics-setting labels are present there, but its serialization format has not yet been verified well enough for a public parser.

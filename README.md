# Ancestors Enhanced Configurator

An unofficial, portable configurator for *Ancestors: The Humankind Odyssey*.

The repository is in its foundation phase. The current application deliberately does not read or modify game files. The first functional milestone will detect supported installations and display configuration state without writing anything.

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

This is development software and not yet a mod release. It must not be presented as an official Panache Digital Games or Private Division product.

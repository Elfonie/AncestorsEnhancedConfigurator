# Ancestors Enhanced Configurator

An unofficial, portable configurator for *Ancestors: The Humankind Odyssey*.

Version 0.3 reads the installed Steam build and safely edits a reviewed set of
`Engine.ini` graphics overrides. It does not install a service, modify the
registry, use telemetry, or access the network.

## What 0.3 can do

- Detect the native Windows Steam installation and build ID.
- Explain current overrides and the verified Low, Medium, and High preset values.
- Switch between essential and advanced settings.
- Edit 44 typed renderer settings with bounded controls instead of free text.
- Keep edits pending until the user explicitly applies them.
- Refuse writes while the game is running or when the file changed after preview.
- Preserve unrelated INI entries, comments, encoding, and line endings.
- Create an operation manifest and exact backup before every write.
- Undo the latest operation only while its result is still unchanged.

`System.sav` values and PAK contents remain read-only. The known half-strength
vignette patch can be detected, but 0.3 does not rewrite or generate PAK files.

## Supported editing target

- Store: Steam
- Host: native Windows
- Game build: `5495393`
- File: `%LOCALAPPDATA%\Ancestors\Saved\Config\WindowsNoEditor\Engine.ini`

Other builds can still be inspected, but editing is disabled until their
configuration tables have been checked.

## Development

Requirements: .NET 10 SDK and Git.

```text
dotnet build AncestorsEnhanced.slnx
dotnet test AncestorsEnhanced.slnx
dotnet run --project src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj
```

This project is not affiliated with Panache Digital Games or Private Division.

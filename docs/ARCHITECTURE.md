# Architecture

## Dependency direction

```text
AncestorsEnhanced.App
        |
        +--> AncestorsEnhanced.Core
        |
        +--> AncestorsEnhanced.Infrastructure --> AncestorsEnhanced.Core
```

`Core` contains the product rules and must not depend on a UI framework, a particular operating system, Steam, Epic, GOG, or Unreal asset tooling.

`Infrastructure` implements access to operating systems, stores, files, processes, hashes, INI files, and later PAK tooling.

`App` displays state and invokes application operations. It must not implement file mutation inside button handlers.

The readable settings catalog models a feature hierarchy rather than exposing one UI card per console variable. A feature group owns its summary and related settings; each setting records its source, technical key, confidence state, and whether it belongs in the advanced view. The application may filter this hierarchy, but it must not reinterpret unknown values as detected defaults.

## Configuration operation lifecycle

Every game modification implements the same lifecycle:

1. Detect current state.
2. Produce a change plan.
3. Show the plan to the user.
4. Apply transactionally.
5. Validate the resulting state.
6. Revert only an unchanged, owned result.

Version 0.3 implements this lifecycle for a reviewed set of `Engine.ini` keys.
The UI creates typed requests; Core validates their build, type, range, and
target; Infrastructure preserves the source document and owns the file
transaction. The application never writes from a button handler directly.

## Inspection boundary

Inspection remains exposed through `IReadOnlyGameInspector`. Its physical
file-system dependency still has no write methods. Mutation is isolated behind
`IGameSettingsEditor`, so discovery cannot acquire write access accidentally.

The Windows Steam inspector separates:

- host operating system;
- storefront;
- compatibility layer;
- Steam root and library root;
- game installation and per-user data;
- raw INI entries and PAK metadata.

This separation prevents later Epic, GOG, Proton, and Wine support from being represented as variants of a hard-coded Steam path.

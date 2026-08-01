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

## Planned operation lifecycle

Every future game modification must implement the same lifecycle:

1. Detect current state.
2. Produce a change plan.
3. Show the plan to the user.
4. Apply transactionally.
5. Validate the resulting state.
6. Revert only owned changes.

The foundation phase has all mutation capabilities disabled.

## Read-only inspection boundary

Version 0.2 exposes only an `IReadOnlyGameInspector` to the application. Its physical file-system dependency contains methods for existence checks, metadata, enumeration, and text reads; it deliberately contains no create, write, move, or delete operation.

The Windows Steam inspector separates:

- host operating system;
- storefront;
- compatibility layer;
- Steam root and library root;
- game installation and per-user data;
- raw INI entries and PAK metadata.

This separation prevents later Epic, GOG, Proton, and Wine support from being represented as variants of a hard-coded Steam path.

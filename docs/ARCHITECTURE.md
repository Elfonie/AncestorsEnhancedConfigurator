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

## Planned operation lifecycle

Every future game modification must implement the same lifecycle:

1. Detect current state.
2. Produce a change plan.
3. Show the plan to the user.
4. Apply transactionally.
5. Validate the resulting state.
6. Revert only owned changes.

The foundation phase has all mutation capabilities disabled.

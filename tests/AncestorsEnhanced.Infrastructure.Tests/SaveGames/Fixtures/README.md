# F028 - External validation gate (real savegames)

This document records the **external, manual release gate** for cheat validation. It is
the only part of the 0.9 work that genuinely requires real-world data that is NOT present
in this repository. Everything code-side around cheats is implemented and verified with
synthetic schema fixtures (see `../F027CheatTargetSpecTests.cs`); what is missing is
validation against real (anonymised) save files and an in-game run.

## What is already proven by synthetic fixtures

The structural target model (`CheatTargetSpec`, F027) is implemented and tests cover:

- exact schema-path targeting (a same-named property at another path is never patched),
- right path / wrong type and right name / wrong parent are rejected,
- more matches than authorised fail closed,
- compress/decompress round trip, range containment and byte-for-byte diff confinement.

The following targets are wired with test-verified (synthetic) schema paths:

| Cheat | Property / path | Type |
|-------|-----------------|------|
| MaxNeuronalEnergy | `<save>/RPGData/NeuronalEnergySources` | FloatProperty array |
| MaxNeeds | `<save>/PlayerControllerData/CharacterData/VitalityData/{RegimenStamina,Energy,Stamina}` | FloatProperty |
| HealClan | `<save>/PlayerControllerData/CharacterData/{VitalityData/{Energy,Stamina},HealthData/Health}` | FloatProperty |

`ForceMutations` has **no** verified real-world path: without a fixture it stays
fail-closed (the cheat reports "no supported fields" and changes nothing).

## Fixtures needed before this gate can be closed

1. **Anonymised lineage savegames** (one per slot used in the UI, slots 0-4), real
   `Savegame{N}.sav` files, scrubbed of any personal identifiers before being committed.
   Place them in this `Fixtures/` folder as:
   - `slot0.sav`, `slot1.sav`, ... (`slot0.sav` is the reference used by the gate tests).
2. **One System.sav** (`System.sav`) used to lock the System.sav settings gate.
3. **One in-game validation run report**: start the game after applying each cheat,
   confirm the value actually took effect (neuronal energy, needs/health values, free
   camera toggle) and record the result.

## What the gate then proves

- Injector + post-reparse verification agree on the exact same real target paths.
- Each target's range is fully inside exactly that node, the type is right, the value is
  right, and bytes outside the reported ranges are unchanged.
- Same-named nodes anywhere else in a REAL save are never accepted by accident.

## How to run

Once `slot0.sav` is present, a fixture-backed test is intended to load it and assert the
cheat targets resolve and stay confined. Until a real fixture lands, no such test is
added (would fail), and the F028 gate remains an explicit manual release step.

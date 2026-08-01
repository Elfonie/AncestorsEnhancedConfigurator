# Version 0.3 limitations

## Compatibility

- Editing is enabled only for native Windows, Steam build `5495393`, and the
  normal `%LOCALAPPDATA%\Ancestors\Saved` directory.
- Epic, GOG, manual installations, Linux, Proton, and Wine are not detected for
  editing yet.
- A different game build remains read-only until its settings have been checked.

## Settings not controlled

- `System.sav` is detected but not decoded or written. Resolution, display mode,
  VSync, frame limit, the active quality preset, and other values owned only by
  that binary file remain game-controlled.
- PAK files are read-only. The known half-strength vignette patch can be detected,
  but the app cannot install, remove, generate, or scale it.
- Version 0.3 exposes 44 reviewed renderer overrides and one No-Intro option. It
  does not make every discovered console variable editable.
- It does not add HDR, XeSS, DirectX 12, new assets, shaders, or engine features.
- It does not benchmark the PC or choose settings for a target frame rate.

## Safety boundary

- Applying and restoring require the game to be closed.
- Undo restores only the newest configurator-owned operation whose resulting
  files are still unchanged. Repeating Undo can walk back more than one operation
  while that condition continues to hold.
- Each file replacement is atomic, but a two-file change is not a filesystem
  transaction. A power loss or forced process termination between the two file
  replacements can leave a partial operation. Backups remain on disk, but 0.3
  does not offer automatic crash recovery.
- Hashes detect changes between Review and Confirm, but the app does not take an
  exclusive lock against every other program for the entire operation.
- Validation proves that the requested files contain the requested text. It does
  not prove the visual result inside every scene of the game.

## Product features still missing

- No profiles, import/export, search, localization, or automatic recommendations.
- No signed self-contained release archive, updater, or public release workflow.
- No integrated full backup browser; only safe newest-operation Undo is exposed.

The `Engine.ini` apply/reload and repeated Undo path has been tested on the real
installation. The new No-Intro write and combined `Engine.ini` plus `Game.ini`
operation currently have automated temporary-file coverage but still need a live
game test before a public release claim.

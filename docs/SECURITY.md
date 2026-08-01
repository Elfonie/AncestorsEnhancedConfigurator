# Safety and security rules

The configurator will not require administrator privileges for normal use.

## Non-negotiable rules

- Never write before the target game build and file state have been detected.
- Never overwrite an original PAK.
- Never replace a complete INI merely to change individual keys.
- Never restore a backup blindly after another program has changed the same file.
- Never execute a shell command assembled from untrusted input.
- Never download or execute tools at runtime without an explicit future design review.
- Never collect telemetry or contact a server without a separately reviewed opt-in feature.
- Preserve evidence and stop when ownership or file state is ambiguous.

## Version 0.3 write boundary

- Writes are limited to reviewed keys in `Engine.ini` and the No-Intro key in
  `Game.ini` for the native Windows Steam build 5495393.
- The target must be a normal file in the detected `WindowsNoEditor` directory;
  linked targets and linked configuration directories are rejected.
- The user-data root must match the current Windows user's native
  `%LOCALAPPDATA%\Ancestors\Saved` path.
- The game must not be running.
- Values are validated against typed choices or bounded numeric ranges.
- A visible review is required before writing; its internally fingerprinted plan
  is valid once and is rejected if replaced, replayed, or modified.
- The current file hash must match the preview hash.
- Changes spanning both allowed INI files are backed up, applied, and rolled back
  as one operation.
- The existing file is backed up before replacement and the written hash is checked.
- Backup bytes are hash-checked before use; linked backup history is rejected.
- Rollback is offered only for the newest owned operation and only while its
  resulting file hash is unchanged.
- `System.sav` and PAK files are never write targets.
- Network access and telemetry remain disabled.

## Version 0.2 read protections

- The application explicitly runs with the current user's privileges and does not request elevation.
- Steam manifests must identify App ID 536270.
- The manifest's install directory must be one safe directory name; separators, `.` and `..` are rejected.
- Text metadata and INI files larger than 4 MiB are not read.
- PAK contents are not loaded; only directory metadata is inspected.
- Patch-style PAK naming is reported as a classification, not proof of authorship or purpose.

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

## Foundation state

Game-file writes, network access, and telemetry are disabled in the application safety profile.

## Version 0.2 read protections

- The application explicitly runs with the current user's privileges and does not request elevation.
- Steam manifests must identify App ID 536270.
- The manifest's install directory must be one safe directory name; separators, `.` and `..` are rejected.
- Text metadata and INI files larger than 4 MiB are not read.
- PAK contents are not loaded; only directory metadata is inspected.
- Patch-style PAK naming is reported as a classification, not proof of authorship or purpose.

# Roadmap

## 0.1 Foundation

- Cross-platform solution structure
- Read-only safety profile
- Automated build and tests
- Architecture and security documentation

## 0.2 Read-only detection

- Detect Windows Steam installation
- Detect Ancestors user-data directory
- Display installed PAKs and configuration files
- Identify known custom modifications without changing them
- Model Steam, Epic, GOG, Proton, Wine, and manual locations

## 0.3 Change planning

- Settings catalog
- Current, original, recommended, and desired values
- Human-readable preview
- No writes yet

## 0.4 First reversible write

- One verified INI setting
- Backup and operation manifest
- Post-write validation
- Conflict-aware rollback

Public releases remain out of scope until the supported build matrix and rollback behavior have been tested.

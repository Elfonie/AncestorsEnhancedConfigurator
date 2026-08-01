# Roadmap

## 0.1 Foundation

- [x] Cross-platform solution structure
- [x] Read-only safety profile
- [x] Automated build and tests
- [x] Architecture and security documentation

## 0.2 Read-only detection

- [x] Detect a native Windows Steam installation and its build ID
- [x] Detect the Ancestors user-data directory
- [x] Read every top-level INI while preserving duplicate keys
- [x] Detect the separate binary `System.sav` settings source
- [x] Display installed PAK metadata without reading PAK contents
- [x] Classify patch-style packages without assuming their origin
- [x] Show verified overrides as human-readable setting cards
- [x] Group related settings into expandable visual features
- [x] Provide simple and advanced read-only views
- [x] Catalogue the fields used by Ancestors' custom scalability configuration
- [x] Fingerprint small patch packages without hashing multi-gigabyte base archives
- [x] Model host, store, and compatibility layer independently
- [x] Test alternate Steam libraries and unsafe manifest paths

Epic, GOG, Proton, Wine, and manual-path discovery remain future compatibility work; their data model exists, but detection is not claimed yet.

## 0.3 Safe INI editing

- [x] Separate readable values from typed editing rules
- [x] Essential and advanced editing interface
- [x] Pending changes before apply
- [x] Separate old-to-new review and confirmation step
- [x] 44 bounded `Engine.ini` controls for verified build 5495393
- [x] Typed No-Intro control in `Game.ini`
- [x] Single-use, fingerprinted change plans
- [x] Coordinated multi-file apply and rollback tests
- [x] Preserve unrelated INI content and text format
- [x] Hash-based conflict detection
- [x] Backup and operation manifest
- [x] Post-write validation
- [x] Conflict-aware rollback of the latest owned operation
- [x] Refuse writes while Ancestors is running
- [x] Keep `System.sav` and PAK modification disabled

## 0.4 Portability and profiles

- User-facing presets without hiding their individual changes
- Export and import a portable settings profile
- Manual installation selection
- Epic and GOG discovery
- Proton and Wine path discovery
- Signed portable release archives

Public releases remain out of scope until the supported build matrix and rollback behavior have been tested.

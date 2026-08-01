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

## 0.3 Change planning

- [x] Initial read-only settings catalog
- Current, original, recommended, and desired values
- Verified read-only decoding strategy for supported `System.sav` values, or an explicit decision to leave them game-managed
- Human-readable preview
- No writes yet

## 0.4 First reversible write

- One verified INI setting
- Backup and operation manifest
- Post-write validation
- Conflict-aware rollback

Public releases remain out of scope until the supported build matrix and rollback behavior have been tested.

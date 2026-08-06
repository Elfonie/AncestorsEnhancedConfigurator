# HANDOFF — Ancestors Enhanced Configurator

**Stand:** 2026-08-06 · lokaler Hardening-Durchlauf (Version 0.9.0, keine Git-Operationen)

## Hardening-Durchlauf 2026-08-06

- Checkpoint-IDs strikt validiert (Pfad-/Containment-Checks, keine leeren IDs, Max-Länge 32).
- Cheat-Float-Arrays strikt an den Node-Payload gebunden; Pre-Store-Validierung (Roundtrip + Feld-/Offset-Check).
- Free Camera: Ownership-Flag, Erhalt fremder ConsoleKeys, idempotent.
- Watchdog: Fehler werden gemeldet statt verschluckt, Cooldown nur nach echtem Checkpoint, Error-/Renamed-Handling.
- Checkpoints transaktional (Temp-Ordner + Validierung vor atomarem Move).
- Settings-Rollback unterscheidet Applied/RolledBack/PartialRollbackRequired.
- Settings-Store resilienter (beschädigte Einträge übersprungen, Aufbewahrungsgrenze 50).
- Tool-Settings atomar gespeichert, Cooldown geclampt, Dispose blockiert nicht endlos.
- Single-Instance-Schutz + „--version“-CLI.
- Backend-Validierung berücksichtigt aktuelle Werte; Installationen deterministisch (Steam>Epic>GOG>Heroic).
- UI: Scan-Retry begrenzt (3), „Scan again“-Button, Slot-1-/Dateiname-/KB-MB-Anzeige, Karten-Hover nur interaktiv, DataContext-Abmeldung.

## Projekt

- **Zweck:** Portabler Desktop-Konfigurator für **Ancestors: The Humankind Odyssey** (Steam/Epic/GOG/Heroic, Windows + Linux/Proton). Grafik-Einstellungen editieren (INI + System.sav + PAK), Savegame-Checkpoints verwalten, Cheats sicher anwenden, Free-Cam einrichten.
- **Stack:** .NET 10, **Avalonia 12.x** (Cross-Platform-UI, MVVM), CommunityToolkit.Mvvm.
- **Projekte:** `src/AncestorsEnhanced.Core` (Domäne/Logik), `src/AncestorsEnhanced.Infrastructure` (Datei-/INI-/System.sav-/PAK-/Savegame-Zugriff, Store-Erkennung, Logger), `src/AncestorsEnhanced.App` (Avalonia-UI + ViewModels), `tests/…` (Core/Infrastructure/App).

## Build & Test (wie CI)

```powershell
dotnet clean AncestorsEnhanced.slnx -c Release
dotnet restore AncestorsEnhanced.slnx
dotnet build  AncestorsEnhanced.slnx -c Release --no-restore
dotnet test   AncestorsEnhanced.slnx -c Release --no-build --nologo
```

Einzelne Tests: `dotnet test "tests\<Projekt>\<Projekt>.csproj" -c Release --no-build --filter "FullyQualifiedName~<Name>"`

## Features (Stand 0.8.3)

- **Grafik:** Simple/Advanced; Suche (modus-unabhängig, 250 ms Debounce async/await); Review-vor-Write mit Backups/Undo/Restore-Game-Defaults; System.sav-Presets (Low/Medium/High).
- **Qualitätsanzeige:** zentrale `MapQuality`-Abbildung → **Off/Low/Medium/High/Ultra** (statt „Quality 1-5“/„Level 1-4“). Kompakt-Zusammenfassungen zeigen volle Begriffe.
- **Savegames:** 5 Slots, Checkpoints (max 50/Slot), Auto-Backup-Watchdog (Debounce 500 ms), Load/Delete mit Bestätigungs-Flyout (schließt nach Bestätigung automatisch), „Show older/less“, Steam-Cloud-Hinweis.
- **Cheats:** Max Neuronal Energy, Max Needs, Heal Clan — als neue Checkpoints (Original nie überschrieben). **Free Camera** = **INI-Tweak** (`Input.ini` → `ConsoleKeys=F10`, mit Backup), kein Savegame-Cheat; im UI getrennt „INI TWEAKS“ vs. „SAVEGAME CHEATS“.
- **Erkennung:** Steam/Epic/GOG/Heroic (Windows + Linux/Proton); User-Data-Auto-Erkennung; fehlende User-Data-Warnungen verschwinden per Retry-Timer automatisch.

## Wichtige Architektur-/Stabilitätsentscheidungen

- **UI-Thread:** Watchdog-Refresh wird auf den UI-Thread gemarshalt; globaler Busy-Zustand `IsAnyOperationRunning` (MainVM + SaveManager + Cheat) über Child-`PropertyChanged`-Abos mit sauberer Abmeldung.
- **CTS-Lebenszyklus:** Such- und Watchdog-Debounce nutzen **nur `Cancel()`**; Watchdog-`CancellationTokenSource` wird ausschließlich im `finally` des eigenen Tasks disposet.
- **I/O-Serialisierung:** `SaveSettings` schreibt via `Task.Run` + `SemaphoreSlim` (keine parallelen Dateizugriffe).
- **Cheat-Prozess-Check:** `Process.GetProcessesByName` läuft in `PeriodicTimer`-Hintergrundloop; UI wird nur bei Statuswechsel benachrichtigt.
- **Directory Traversal:** `IsSafeSingleDirectoryName` (Steam-Manifest `installdir`) prüft **explizit `/` und `\`** als Manifestsyntax (hostunabhängig) und weist `.`/`..` ab.
- **Review-Modal:** im Root-Grid über die ganze App (`Grid.ColumnSpan=2`), blockiert Sidebar+Bottom-Bar, Escape + Backdrop-Klick, Cancel/Confirm-Buttons **im Modal**; globales Lade-Overlay sperrt die UI mechanisch.
- **Kultur:** UI-Formatierung benutzt `CurrentCulture` (Desktop-App: de `1,46 GB`, en `1.46 GB`); technische INI-/System.sav-Werte bewusst `InvariantCulture`. Kultur-Tests setzen die Kultur explizit und stellen sie im `finally` wieder her.

## Design („Primal Survival“-Theme)

Sidebar-Navigation, warm-dunkle Palette (Kohle `#070907`, Flora `#B4D941`, Feuer `#FF5A00`, Blut `#D92316`, Knochenweiß), Gradient-Statusboxen, `SettingCard`-Floating-Look mit Hover-Glow, `PrimaryAction`/`DangerAction`-Gradient-Buttons, ToggleSwitch grün via `ToggleSwitchCurtain*`-Keys, Window ohne feste Width/Height.

## Offene Punkte / nächste Schritte (Empfehlung)

- **Commit/Push des aktuellen Stands** prüfen (Arbeitsbaum sauber; gewünschte Änderungen sind offenbar bereits committet).
- **Release-Package bauen:** `dotnet publish src/AncestorsEnhanced.App/AncestorsEnhanced.App.csproj -p:PublishProfile=win-x64` und `linux-x64`, ZIP + SHA256SUMS (siehe `artifacts/`, README).
- **Optional (bewusst zurückgestellt):** Quick-Presets (Max Performance/Balanced/Ultra) im Graphics-Tab; weiterer Design-Feinschliff.
- **Visuell am laufenden Fenster prüfen:** Delete-Flyout schließt nach Bestätigung; Lade-Overlay sperrt Eingaben; ToggleSwitches grün.

## Arbeitshinweise

- Keine Cheat-/Savegame-Parser-Änderungen ohne expliziten Auftrag (Cheat-Injektion ist bewusst „safe float injection“, kein Clan-/Mutations-Pfad).
- `docs/HANDOFF.md` gepflegt halten; nie mit Binär-/Debug-Artefakten füllen.
- Zeilenenden: C#/XAML teils gemischt; Ersetzungen robust über Node (UTF-8) statt PowerShell-`Get-Content`-Anzeige.
- Keine `dbg_arr`, `clan_probe`, `clan_hex`-Dateien erzeugen; keine Spielstände verändern.

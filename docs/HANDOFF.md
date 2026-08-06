# Übergabe / Handoff – Ancestors Enhanced Configurator

Stand: 2026-08-06 · Alle Angaben gegen den echten Code verifiziert.

## Projekt in Kürze

Portabler Konfigurator für **Ancestors: The Humankind Odyssey** (Grafik, Saves, Cheats).
- Pfad: `C:\Users\Firefly\Documents\PCSTUFF\AncestorsEnhancedConfigurator`
- Stack: .NET 10 (SDK 10.0.302, global.json), Avalonia 11, CommunityToolkit.Mvvm
- Schichten: `src/AncestorsEnhanced.Core` (Domäne), `src/AncestorsEnhanced.Infrastructure` (Datei/Save/PAK/INI), `src/AncestorsEnhanced.App` (Avalonia-UI), `tests/*`
- Build: `dotnet build AncestorsEnhanced.slnx` → 0 Warnungen/Fehler
- Tests: `dotnet test AncestorsEnhanced.slnx` → **122/122 grün** (Core 15, Infra 73, App 34)

**Wichtig:** Alle Änderungen sind UNCOMMITTET (kein git commit, kein Release-Package). `.github/workflows/build.yml` (Windows+Linux) und `LICENSE` (MIT) liegen bereits im Arbeitsbaum.

## Was der letzte Stand kann (Features)

- **Graphics:** Simple/Advanced, Suche (nur Advanced), Review-vor-Write mit Backups/Undo, System.sav-Presets (Low/Medium/High), Quality-Anzeige Off/Low/Medium/High/Ultra, Preset/Override-Erklärungskarte (Game preset + Custom overrides-Anzahl).
- **Saves:** 5 Slots, Checkpoints (max 50/Slot) mit Herkunfts-Label (`Manual`/`AutoBackup`/`PreRestore`/`Cheat:X`), Auto-Backup mit Mindestabstand, Restore mit Bestätigung, Steam-Cloud-Hinweis.
- **Cheats:** Max Neuronal Energy, Max Needs, Heal Current Ape (pfad-begrenzt), Free-Cam (INI, sofort), „Create checkpoint" + „Restore this cheat checkpoint now"-Flow.
- **Sicherheit:** Transaktionale Writes, SHA-256, atomarer Move, Pfad-Guards, Snappy-Validierung, Checkpoint-Manifest mit Hash.

## Bewusst getroffene Entscheidungen (nicht „vergessen")

- **Cheat-Injektion ist pfad-begrenzt** (Release-Blocker aus der letzten Bewertung): `SaveGameCheatInjector` patcht NUR
  - `MaxNeuronalEnergy` → `RPGData/NeuronalEnergySources` (Array)
  - `MaxNeeds` → `PlayerControllerData/CharacterData/VitalityData/{RegimenStamina,Energy,Stamina}`
  - `HealClan` (heißt jetzt **Heal Current Ape**) → `PlayerControllerData/CharacterData/{VitalityData, HealthData}`
  - Fremde gleichnamige Felder bleiben unangetastet (Test `HealClanDoesNotTouchUnrelatedHealthFields`).
- **„Heal Clan" → „Heal Current Ape"** umbenannt, weil der verifizierte Pfad nur den aktiven Charakter trifft. Echte Clan-Mitglieder liegen im `CharacterDataList`-Array (`PlayerClanData`), dessen Elementgrenzen noch NICHT deterministisch verifiziert sind (im realen Savegame4.sav ist nur Element 0 gefüllt; Count=5 ist Kapazität). Clan-Heilung = offene Forschungsaufgabe.
- **Restore-Flow gibt echte Ergebnisse zurück:** `SaveManagerViewModel.RunLoad` → `Task<SaveGameOperationResult>`, `RunOperation` → `Task<SaveGameOperationResult>`; Cheat-Callback ist `Func<string,string,Task<SaveGameOperationResult>>`; `RestoreLastCheckpointAsync` zeigt nur bei `Succeeded` grünen Erfolg, sonst rote Fehlermeldung; Guard `if (!CanRestoreLastCheckpoint) return;` + `NotifyState()` benachrichtigt auch `CanRestoreLastCheckpoint`.
- **CustomOverrideCount** zählt jetzt `_editors.Values.Count(e => e.HasActiveOverride)` (nur entfernbare Overrides, nicht System.sav-Presets).

## Was heute behoben wurde (Stand 2026-08-06)

Alle 15 Punkte aus der letzten Übergabe sind im Arbeitsbaum umgesetzt, lokal verifiziert:
Build (Debug+Release) 0 Warnungen/Fehler; Tests 122/122 grün (Core 15, Infra 73, App 34).

- **CI gehäert (Punkt 1):** `.github/workflows/build.yml` pinnt nun `dotnet-version: 10.0.302` (statt `10.0.x`) bei `global-json-file: global.json`; lokale Release-Wiederholung des CI-Pfads (restore → build -c Release → test -c Release) grün.
- **Detection-Statusbox (2):** Kasten bindet jetzt `Background`/`BorderBrush` an `DetectionColor`, Text lesbar auf Akzent (dunkle Schrift), Statusfarben wie gehabt (Scanning=orange, Ready=grün, Problems=gelb, Failed=Lachsrot, Not found=grau).
- **Save-Statusbox (3):** Kasten bindet Border/Text an `SaveManager.StatusAccent` statt fest grün.
- **Leere Slots (4):** Bei `HasNoSlots` werden keine leeren Slot-Karten mehr gelistet; stattdessen echte Leeransicht „No save games found yet" mit Hinweis + `Scan for save games`-Button.
- **Bestätigungen schließen sich aus (5):** `SaveGameCheckpointViewModel` blendet beim Öffnen von Delete das Restore-Flyout aus und umgekehrt.
- **Cheat-Slots (6):** ComboBox hat ItemTemplate `Label`; neu: Slots werden nach Save-Existenz als `Slot N · saved` / `Slot N · empty` angezeigt (via `CheatViewModel.UpdateSlotAvailability`, von `MainViewModel.RefreshFromDiskAsync` aufgerufen).
- **Header (7):** „God Mode / Cheats" → „Cheats".
- **Override-Label (8):** Checkbox heißt jetzt „Use custom value"; Fehlermeldung „Unsupported value. Turn off „Use custom value“ to reset it."; Runtime-Hinweis blieb knapp.
- **Preset-Karte (9):** nur bei `HasGamePreset` sichtbar (Spiel installiert).
- **Kontraste (10):** Grau-Töne heller (`#697B86→#788892`, `#647580→#74838D`, `#5F7480→#6F828D`), Fehlerfarbe Lachsrot `#E04D42` (4,69:1 auf Dark) statt `#D92316` (3,72:1); `App.axaml`-Resourcen `DangerBrush`/`BloodBrush` und der DangerAction-Verlauf ebenfalls heller.  
- **Accessibility (11):** AutomationProperties.Name an Free-Cam-/Toggle-/Watchdog-/Zahlenfeld-Steuerelementen; Review-Overlay bekommt `.Name`, Fokus beim Öffnen auf ersten Button (Cancel), beim Schließen zurück zum Auslöser.
- **Responsive (12):** `MinWidth` 940→760; Einstellungszeilen nutzen `Auto,190,330` damit schmale Fenster die rechten Spalten nicht mehr hart kappen.

Technische Schulden (13–15) bleiben offen: Avalonia-Headless-Tests, MainViewModel-Aufteilung, ForceMutations.

## Wie man den Stand verifiziert

```powershell
cd C:\Users\Firefly\Documents\PCSTUFF\AncestorsEnhancedConfigurator
dotnet build AncestorsEnhanced.slnx -c Debug
dotnet test AncestorsEnhanced.slnx -c Debug --no-build
```

## Arbeitsweise-Hinweise (aus früheren Runden verifiziert)

- C#/XAML nutzen gemischte Zeilenenden (CRLF/LF). Textersetzungen über Node/Python (UTF-8, CRLF-tolerant), nicht über PowerShell-Get-Content-Anzeige interpretieren.
- `TreatWarningsAsErrors` ist aktiv – auch Code-Analyse-Warnungen (z.B. CA1859) brechen den Build.
- Bei `dotnet test`-Hängern: verwaiste testhost/dotnet-Prozesse können DLLs sperren → per `Get-CimInstance Win32_Process` + `Stop-Process` beenden.
- Echte Savegames zum Verifizieren: `C:\Users\Firefly\AppData\Local\Ancestors\Saved\SaveGames\Savegame0.sav`, `Savegame4.sav` (Snappy-komprimiert; `SnappyBlockCodec.Decode` → Tagged-Property-Baum).

# PC Setup Copier

## M2.6 — Unified batch apply and update-check optimization

- The Free Settings tab now has a single **選択した設定を一括適用** button and explicit selection checkboxes for mouse acceleration and local installed power plans. Individual apply actions are hidden; the user confirms a combined change preview once.
- Both settings are read before applying. The batch handler backs up each changed setting, verifies results, and attempts to restore the original in-memory values if any step fails. Existing per-setting backup history and manual restore remain available.
- Other gaming settings, including Game Mode, remain read-only. The batch handler does not write Windows security settings and cannot apply arbitrary registry data.
- Update discovery runs two independent GitHub CLI checks concurrently and removes the redundant standalone authentication command. Update progress now includes elapsed seconds. Download is still the complete signed-by-hash-checked Actions artifact; changing the transfer mechanism is future work.
- Windows package preflight checks the batch engine's read-only safety contract. Live batch application/rollback and total update speed require local testing.

## M2.5 — Installed power plans and responsive UI

- The Free Settings tab now lists only power schemes already installed on the local PC and shows the currently active GUID.
- Only confirmed local schemes can be activated, with explicit confirmation, a timestamped backup of the previous GUID, read-back verification and attempted rollback. Restore is separate on the Backup & Restore tab. Power plans are not copied blindly between different PCs.
- The change uses the documented Windows powercfg /list and /setactive operations and the PowerGetActiveScheme API. It does **not** import/create/delete schemes, edit individual power parameters, modify Windows Power Mode or elevate permissions.
- Mouse-only preset storage and copy-from-JSON behavior remain unchanged. Game Mode and other settings remain read-only.
- Window minimum size is reduced to support smaller displays; remaining layout polish needs actual device testing.
- This implementation is ready for Windows build verification but **power-plan apply/restore requires explicit local test** before use on a friend's computer.

## M2.4 — Named preferences and compact diagnostics

- The Diagnostics tab uses a sortable-by-reading compact table instead of a long continuous text dump. All 11 existing diagnostic results remain read-only.
- The Free Settings tab saves, loads and deletes named mouse-acceleration preferences, locally at `%LOCALAPPDATA%\\PCSetupCopier\\Presets\\presets.json`. At most 100 presets, with name validation and atomic replacement.
- Preset loading only updates the selection on screen; the user must separately press Apply and confirm before any Windows setting changes.
- Presets are **not** backups. The separate before-change mouse backup/restore capability remains unchanged.
- Package preflight verifies the safe capability registry and the preset serialization/name validation contract, but live mouse changes still require Windows device testing.
- The 10 other settings remain read-only while their Windows-version-specific APIs and rollback methods are evaluated.

## M2.3 — Free configuration and tabbed UI

- Four pages: diagnosis, free configuration, import/copy from another PC, backup/restore.
- Free configuration does **not require a JSON file**. It uses the same M2.2 capability-checked engine, confirmation screen, automatic backup, read-back verification and restore.
- Current writable setting: mouse acceleration only. Windows mouse acceleration off is written as [0,0,0], on as [6,10,1] (Win32 SPI_SETMOUSE); Windows Settings should be checked during device testing.
- The copy/import workflow remains available on its own page. All 10 other settings are diagnostic/comparison-only.
- Future writes must be individually tested on supported Windows versions; the tool never modifies Defender, firewall, updates, SmartScreen, UAC, encryption or other security protections.

## M2.2 — Common setting engine (2026-11-09)

The setting engine now registers all 11 existing gaming settings behind a common handler contract:
capability detection, current-value read, desired-value validation, apply, and restore.
A single change-plan builder rejects unavailable/unknown settings and unsupported writes.
The UI uses that plan for enabling its Apply button and for confirmation of changes.
M2.2 keeps the original JSON import/export and GitHub self-update workflow.

**Write allowlist:** only `mouse_acceleration` has an applying handler; it delegates to the existing backed-up and verified Windows mouse settings implementation.
All other 10 settings are read-only adapters; unrecognized IDs (including security controls) are rejected.
The CI package preflight checks the expected registry and the single-writer restriction without changing any Windows setting.
Actual cross-version testing and applying multiple settings transactionally remain future work.
The target architecture is Windows 10 and 11 with explicit per-setting capability checks, not blind registry value copying.


Windows 11 gaming-related settings diagnostic and comparison app, built with C# WinForms and .NET 8.

## Current release: M2.6 (selectable batch apply and faster update check)

- Diagnose 11 gaming-related settings: refresh rate, Game Mode, power plan, graphics settings, capture, FPS limit, mouse acceleration, HAGS, windowed optimization, VRR, per-app GPU preferences.
- Show current value, read status, and explanation.
- Export a JSON snapshot and import one from another PC.
- Select individual settings as a **preview**. The app does **not** change Windows settings.
- Unknown/unsupported settings stay explicitly unknown; never infer enabled/disabled state.

Security controls, including Defender, firewall, updates, SmartScreen, UAC, memory integrity and encryption, must not be modified by future apply features.

## Run locally

```powershell
dotnet run --project .\PCSetupCopier\PCSetupCopier.csproj -c Release
```

## Windows build

The workflow [Build Windows app](.github/workflows/build-windows.yml) restores, builds and publishes a self-contained win-x64 app as a GitHub Actions artifact. You can retrieve the artifact from the run's page after it finishes.

**Status:** Earlier Windows Actions runs passed. The updated M1.6 workflow and updater still require end-to-end Windows testing.

## Update roadmap

M1.6 adds an **Update check** button to packaged GitHub Actions artifacts. Because this repository is private, the user needs GitHub CLI (`gh`) installed and one-time authenticated (`gh auth login`). The app uses `gh run list`/`gh run download` to acquire the latest successful Windows artifact, verifies its commit against `build-info.json`, and uses a separate short-lived CMD helper to replace the executable after exit (retaining `.previous` backup). The app does not embed GitHub credentials. Local development runs deliberately cannot self-update. The updater has not yet been Windows-runtime tested.

First-time installation: download the `PCSetupCopier-win-x64` artifact from the latest successful Actions run, extract it into a writable user folder, and start `PCSetupCopier.exe`. Install GitHub CLI and run `gh auth login` once to enable in-app updates. Never extract it into a system directory or run as administrator.


## M1.6 update verification

The new Windows workflow packages `PCSetupCopier.exe.sha256`. The updater checks this checksum and the build commit before invoking a short-lived cmd helper to replace the executable after the app exits. The previous executable is retained as `PCSetupCopier.exe.previous`. This is an integrity check against corrupted files, not a substitute for signing/provenance verification. Source and executable remain read-only regarding Windows settings.

M1.6 adds persistent preview checkbox selections and a comparable-items counter. **End-to-end install/update/rollback testing on Windows is pending.**


### M1.6 updater hotfix
The previous M1.5 CMD-based updater may fail when the installation directory contains Japanese characters. M1.6 launches the staged .NET executable as a separate helper instead, with Unicode-safe argument handling, a 45-second wait for the old process, file backups, and a detailed error log. **Because the old updater is broken, one manual installation of the successful M1.6 build may be necessary.** This update mechanism still needs end-to-end Windows testing.


### M1.7 staged-update preflight

Before changing an installed executable, the app now launches the downloaded packaged executable using `--verify-package` and checks that it exits successfully. The Windows Actions workflow runs the same package-level smoke test on every build. The old exe and metadata are still backed up before replacement, with rollback attempted on failure. This guards against broken packaging; it does **not** prove a full update or rollback succeeds on the user's Windows computer. Windows settings remain read-only.


## M2.0 — first safe apply capability

- Windows mouse acceleration is the **only** setting that can be written at this stage. The other 10 items remain diagnostics/comparison-only.
- Import a source JSON snapshot, select **only** the mouse-acceleration checkbox, click **選択項目を適用**, and confirm the displayed before/after values.
- The original 3 SPI mouse parameters are written to a timestamped file and `%LOCALAPPDATA%\\PCSetupCopier\\Backups\\mouse-acceleration-latest.json` before applying.
- The tool calls Windows `SPI_SETMOUSE` with persistence and broadcasts the change, reads back the values, and attempts immediate rollback on error.
- The **マウス設定を復元** button restores the backup. Backups are intentionally retained.
- No registry copying, Defender/firewall/UAC/Windows Update changes, elevation, or writes to other items.
- This version needs a Windows runtime apply/restore test before use on a friend's PC.

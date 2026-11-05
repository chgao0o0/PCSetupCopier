# PC Setup Copier

Windows 11 gaming-related settings diagnostic and comparison app, built with C# WinForms and .NET 8.

## Current release: M1.6 (read-only)

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

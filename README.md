# PC Setup Copier

Windows 11 gaming-related settings diagnostic and comparison app, built with C# WinForms and .NET 8.

## Current release: M1.5 (read-only)

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

**Status:** Earlier Windows Actions runs passed. The updated M1.5 workflow and updater still require end-to-end Windows testing.

## Update roadmap

M1.5 adds an **Update check** button to packaged GitHub Actions artifacts. Because this repository is private, the user needs GitHub CLI (`gh`) installed and one-time authenticated (`gh auth login`). The app uses `gh run list`/`gh run download` to acquire the latest successful Windows artifact, verifies its commit against `build-info.json`, and uses a separate short-lived CMD helper to replace the executable after exit (retaining `.previous` backup). The app does not embed GitHub credentials. Local development runs deliberately cannot self-update. The updater has not yet been Windows-runtime tested.

First-time installation: download the `PCSetupCopier-win-x64` artifact from the latest successful Actions run, extract it into a writable user folder, and start `PCSetupCopier.exe`. Install GitHub CLI and run `gh auth login` once to enable in-app updates. Never extract it into a system directory or run as administrator.


## M1.5 update verification

The new Windows workflow packages `PCSetupCopier.exe.sha256`. The updater checks this checksum and the build commit before invoking a short-lived cmd helper to replace the executable after the app exits. The previous executable is retained as `PCSetupCopier.exe.previous`. This is an integrity check against corrupted files, not a substitute for signing/provenance verification. Source and executable remain read-only regarding Windows settings.

M1.5 adds persistent preview checkbox selections and a comparable-items counter. **End-to-end install/update/rollback testing on Windows is pending.**

# PC Setup Copier

Windows 11 gaming-related settings diagnostic and comparison app, built with C# WinForms and .NET 8.

## Current release: M1.3 (read-only)

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

**Status:** GitHub Actions build and Windows runtime still need verification.

## Update roadmap

M1.3 is *not* self-updating. Planned: signed or integrity-verified release packages, update helper, rollback, and app-internal update check. Private repository release downloads need user authorization; no embedded GitHub tokens or disabled security protections.

The source has been imported into GitHub from the local development version; verify build success before sharing with others.

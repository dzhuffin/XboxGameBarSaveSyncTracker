# Xbox Game Bar Save Sync Tracker

An Xbox Game Bar widget that shows Xbox cloud save sync status for every game on the PC.

## How it works
Gaming Services keeps a local cache per game at
`%LOCALAPPDATA%\Packages\<family>\SystemAppData\wgs\<XUID>_<id>\containers.index`.
The index lists each save container with an ETag that decodes to the server upload time (.NET ticks, UTC).

| State | Rule |
|---|---|
| Syncing | `wgs\t\` contains staging files |
| Pending | a container's local modified time is newer than its ETag time (60 s skew tolerance) |
| Synced | every container's ETag time is at or after its modified time |
| Unknown | no ETag, unreadable index or unsupported index version (only v14 is understood) |

The format is undocumented and reverse engineered, so the parser rejects any version other than 14.

## Projects
- `src/SaveSync.Core` – index parser, state evaluator, scanner, `status.json` model (netstandard2.0)
- `src/SaveSync.Companion` – packaged full-trust .NET 8 process. Watches the `wgs` folders and writes `status.json` (with a 5 s heartbeat) to the package LocalState. `--once` prints a summary; `--out <dir>` overrides the output folder.
- `src/SaveSync.Widget` – UWP Game Bar widget. Launches the companion via `FullTrustProcessLauncher` and polls `status.json`.
- `tests/SaveSync.Core.Tests` – xUnit tests

## Build and install
Requires Visual Studio 2022 with the UWP workload, the .NET 8 SDK and Windows Developer Mode.

```powershell
.\build.ps1 -Configuration Release -Register
```

Debug packages need the .NET Core 2.2 framework package, so register the Release build. Then press Win+G and open **Save Sync Tracker** from the widget menu.

### Installing on another PC (e.g. a handheld)
```powershell
.\build.ps1 -Configuration Release -Package
```
Copy `artifacts\package` to the target PC and run `Install.ps1` from an elevated PowerShell. It trusts the bundled self-signed certificate and installs the signed package with its dependencies; Developer Mode is not needed. The signing certificate is created in `Cert:\CurrentUser\My` and reused on later builds.

## Notes
- Assets are the Microsoft sample placeholders; replace them with your own.
- The manifest uses the restricted capabilities `runFullTrust` and `packageQuery`; check Store policy before publishing.

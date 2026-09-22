# Workday Widget

A minimal Windows 11 Widgets Board card for tracking your workday: clock in with one tap, see your expected finish time at a glance, and get reminded when it arrives.

| Light | Dark |
|---|---|
| <img src="src/WorkdayWidget/ProviderAssets/Workday_Screenshot_Light.png" alt="Workday Widget, light theme" width="300"> | <img src="src/WorkdayWidget/ProviderAssets/Workday_Screenshot_Dark.png" alt="Workday Widget, dark theme" width="300"> |

## Features

- **One-tap clock-in** — press "Clock in now" and the current time is recorded for the day.
- **Expected finish, always visible** — computed from clock-in time plus a configurable workday length.
- **Configurable workday length** — a small accent-colored chip cycles through presets (8, 8.5, 9, 9.5, 10 hours); default is 9.
- **Auto clock-in on unlock** (opt-in) — clocks you in automatically the first time you unlock your PC each day, so the widget never needs to be opened on a normal day.
- **Edit or clear** — fix a wrong clock-in time, or clear today's record entirely (with a confirm step).
- **12/24-hour display** — a one-tap chip next to "Now", independent of the clock-in flow.
- **Finish-time reminder** — one toast notification when your expected finish time arrives; the widget never auto clocks-out or auto-clears.
- **Timezone-safe** — clock-in and finish times always display in your machine's *current* local timezone, even if it changed after you clocked in (travel, a VM); a small note appears only when that adjustment actually happened.
- **Daily reset** — crossing into a new day clears the previous day's clock-in automatically.
- **Small / medium / large layouts**, light and dark themes.
- **Localized**: English, Traditional Chinese, Simplified Chinese (follows Windows' display language automatically).

## Design principles

No dedicated settings screen. Adaptive Cards' action model doesn't have room for one on this host anyway, and a widget you check daily shouldn't behave like a settings form. Every preference (time format, auto clock-in, workday length) lives as a small in-body, one-tap chip instead — visible when relevant, silent otherwise. The footer never carries more than two buttons at once.

## Install

For a production release signed by a trusted provider, users download [WorkdayWidget.appinstaller](https://github.com/klhq/workday-widget/releases/latest/download/WorkdayWidget.appinstaller) and open it with Windows App Installer. It installs the widget and checks for updates automatically.

The current self-signed release is for local development and managed test devices only. Do not share it as a public installer: Windows will block a package whose signer is not already trusted.

## Build and install

This is a Windows Widget provider, so it cannot be run inside a normal Docker container: the Widget host, MSIX deployment, and certificate store are Windows integrations. Instead, the repository provides a Docker-like single build entry point that makes the Windows build reproducible and discoverable.

**Prerequisites**
- Visual Studio 2022 with the ".NET desktop development" and "Windows application development" workloads (provides MSBuild and the Windows App SDK/MSIX packaging tools).
- A code-signing certificate whose subject matches the identity in `src/WorkdayWidget/Package.appxmanifest` (`CN=Workday Widget`). If you don't have one yet, create a self-signed dev cert once:
  ```powershell
  New-SelfSignedCertificate -Type Custom -Subject "CN=Workday Widget" `
    -KeyUsage DigitalSignature -FriendlyName "Workday Widget Dev Cert" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
  ```

**Everyday build**
```powershell
./scripts/Build.ps1
```

**Package, sign, and install**
```powershell
./scripts/Build.ps1 -Configuration Release -Architecture x64 -Package `
  -CertificateThumbprint <thumbprint> -Install
```

The script locates Visual Studio's MSBuild and the newest installed Windows SDK automatically. `-Package` emits the MSIX path; adding `-CertificateThumbprint` signs it from your Current User certificate store, and `-Install` replaces the currently running provider safely. Use `-Clean` to remove this project's generated build and package files before building.

To package without signing or installing:

```powershell
./scripts/Build.ps1 -Configuration Release -Architecture x64 -Package
```

Release and signing instructions for maintainers are in [docs/RELEASING.md](docs/RELEASING.md).

Then press `Win + W`, open "Add widgets," and pin Workday Widget.

## Repository contents

- `src/WorkdayWidget/` — the shipped Windows Widget Provider (Windows App SDK, Adaptive Cards 1.5, COM widget provider model).

## License

This project is available under the [MIT License](LICENSE).

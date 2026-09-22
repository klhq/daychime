# Daychime

A focused Windows 11 widget for tracking your workday: clock in with one tap, see your estimated finish at a glance, and get a reminder when it arrives.

<img src="src/DaychimeWidget/ProviderAssets/Daychime_Screenshot_Light.png" alt="Daychime widget preview" width="300">

## Features

- **One-tap clock-in** — press "Clock in now" and the current time is recorded for the day.
- **Estimated finish, always visible** — computed from clock-in time plus a configurable workday length.
- **Clear end-of-day state** — once the finish time arrives, the widget switches to “Finished at” and shows a completion indicator.
- **Configurable workday length** — a small accent-colored chip cycles through presets (8, 8.5, 9, 9.5, 10 hours); default is 9.
- **Auto clock-in after your chosen time** (opt-in) — records the first eligible unlock in each work cycle. Set 06:00 for a typical day job or 18:00 for a night shift.
- **Edit or clear** — fix a wrong clock-in time, or clear the current shift entirely (with a confirm step).
- **12/24-hour display** — a one-tap chip next to "Now", independent of the clock-in flow.
- **Finish-time reminder** — one toast notification when your estimated finish time arrives; the widget never auto clocks-out or auto-clears.
- **Timezone-safe** — clock-in and finish times always display in your machine's *current* local timezone, even if it changed after you clocked in (travel, a VM); a small note appears only when that adjustment actually happened.
- **Shift-based, not date-based** — a shift can cross midnight. It remains visible after completion until you explicitly start a new workday or clear it.
- **Small / medium / large layouts**, light and dark themes.
- **Localized**: English, Traditional Chinese, Simplified Chinese (follows Windows' display language automatically).

## Design principles

No dedicated settings screen. Adaptive Cards' action model doesn't have room for one on this host anyway, and a widget you check daily shouldn't behave like a settings form. Every preference (time format, auto clock-in, workday length) lives as a small in-body, one-tap chip instead — visible when relevant, silent otherwise. The footer never carries more than two buttons at once.

## Install

Daychime will be installed and updated through Microsoft Store after its first certification. There is no separate installer, certificate, or download step for users.

See the [Privacy Policy](PRIVACY.md).

## Build and install

This is a Windows Widget provider, so it cannot be run inside a normal Docker container: the Widget host, MSIX deployment, and certificate store are Windows integrations. Instead, the repository provides a Docker-like single build entry point that makes the Windows build reproducible and discoverable.

**Prerequisites**
- Visual Studio 2022 with the ".NET desktop development" and "Windows application development" workloads (provides MSBuild and the Windows App SDK/MSIX packaging tools).

**Everyday development build**
```powershell
./scripts/Build.ps1
```

**Create a Store upload package**
```powershell
./scripts/Build.ps1 -Configuration Release -Architecture x64 -StoreUpload -OutputDirectory artifacts
```

The script emits `artifacts\Daychime.msixupload`, ready to upload to Partner Center. Microsoft Store signs and distributes the package; no code-signing certificate is needed. Use `-Clean` to remove this project's generated build and package files before building. The Store is the user installation path after certification; then open `Win + W` → **Add widgets** and pin Daychime.

Release instructions for maintainers are in [docs/RELEASING.md](docs/RELEASING.md).
Store listing copy and screenshot requirements are in [docs/STORE_LISTING.md](docs/STORE_LISTING.md).

## Repository contents

- `src/DaychimeWidget/` — the shipped Windows Widget Provider (Windows App SDK, Adaptive Cards 1.5, COM widget provider model).

## License

This project is available under the [MIT License](LICENSE).

# Daychime

A focused Windows 11 widget for tracking your workday: clock in with one tap, see your estimated finish at a glance, and get a reminder when it arrives.

<img src="src/DaychimeWidget/ProviderAssets/Daychime_Screenshot_Light.png" alt="Daychime widget preview" width="300">

## Features

- **One-tap clock-in** — press "Clock in" and the current time is recorded for the day.
- **Estimated finish at a glance** — the finish time is the largest thing on the card, with the time left and a progress bar; it turns green once the workday is done.
- **Actual clock-out** — clock out when you leave to record the real finish time and elapsed time. Undo an accidental clock-out until the next work cycle starts.
- **Settings in Customize widget** — workday length (8, 8.5, 9, 9.5, 10 hours; default 9), time format (match Windows, 12- or 24-hour) and auto clock-in live in the Widgets Board **⋯ › Customize widget** menu.
- **Auto clock-in after your chosen time** (opt-in) — records the first eligible Windows sign-in or unlock in each work cycle. A background task wakes the widget on sign-in/unlock, so the finish reminder is scheduled without opening the Widgets Board. If the widget starts later, it recovers the timestamp from available Windows session records for your account and session. Opening the panel never substitutes its opening time. If Windows has no accessible record, the widget asks you to clock in and correct the start manually. Set 06:00 for a typical day job or 18:00 for a night shift.
- **Edit or clear** — fix a wrong clock-in time from the medium or large card, or clear the current shift entirely (with a confirm step).
- **Finish-time reminder** — a Windows notification at your estimated finish time, with a **Clock out now** button that records the time you click. Old notifications cannot clock out a newer shift; undo an accidental clock-out in the widget. Windows notification settings apply, and reminders can be missed when the computer is off.
- **Timezone-safe** — clock-in and finish times always display in your machine's *current* local timezone, even if it changed after you clocked in (travel, a VM); a small note appears only when that adjustment actually happened.
- **Overnight and next-day handling** — a shift can cross midnight. A missed clock-out does not block the next eligible auto clock-in: the previous shift is marked as missing an actual clock-out, and the new shift uses the unlock time. A stale shift clears 24 hours after its estimated finish if no new shift starts.
- **Recent shift** — the last shift stays in a compact summary for 24 hours after its actual or estimated finish, clearly distinguishing recorded and missing clock-outs.
- **Small / medium / large layouts**, light and dark themes.
- **Localized**: English, Traditional Chinese, Simplified Chinese (follows Windows' display language automatically).

## Design principles

One glance, one tap. The card answers "when can I leave?" first and offers the single action that fits the moment: clock in, clock out, or undo. Following Microsoft's widget guidance, the main card has at most 1 touch target on Small, 3 on Medium and 4 on Large, and settings live in the host's Customize widget menu instead of in-card pages. Editing the start time and clear confirmation replace the card with a compact form whose Save and Cancel share the input row. See [widget layout checks](docs/widget-layout.md) for size constraints and visual regression checks.

## Install

Get [Daychime from Microsoft Store](https://apps.microsoft.com/detail/9NJKQT7SNSJL) on Windows 11. No manual package download or certificate setup is needed.

After installation, open `Win + W`, open the widget picker, and add Daychime. Microsoft Store handles app updates; use **Check for updates** in the Store to check manually.

The features above describe the current source version. The published Store version may lag behind while an update is awaiting certification.

See the [Privacy Policy](PRIVACY.md).

## Development builds

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

The script emits `artifacts\Daychime.msixupload` for Partner Center, not for end-user installation. Microsoft Store signs and distributes the package; no code-signing certificate is needed for Store submission. Local MSIX installation requires developer signing. Use `-Clean` to remove this project's generated build and package files before building.

## Releases

Development uses `main` with version tags, not a full Gitflow branch model. Pull requests and pushes to `main` run tests and build checks. Pushing a `v*` tag runs tests and produces the `Daychime-store-upload` artifact in the **Build Microsoft Store package** workflow. The tag must match the package manifest version: for example, `v1.0.37` corresponds to `1.0.37.0`. Mismatched tags fail; the workflow does not change the version.

A tag identifies release source, not Store approval. To submit an update, sign in to GitHub, open the matching successful run under [Actions](https://github.com/klhq/daychime/actions/workflows/store-package.yml), and download **Daychime-store-upload** from **Artifacts**. Extract the ZIP and upload `Daychime.msixupload` to Partner Center → **Packages**, then submit for certification.

The workflow handles testing and packaging only; Store submission is manual. No Partner Center credentials, Entra tenant, or signing secrets are needed in GitHub. You can also use **Run workflow** to build an artifact without creating a tag.

Release instructions for maintainers are in [docs/RELEASING.md](docs/RELEASING.md).
Store listing copy and screenshot requirements are in [docs/STORE_LISTING.md](docs/STORE_LISTING.md).

## Repository contents

- `src/DaychimeWidget/` — the shipped Windows Widget Provider (Windows App SDK, Adaptive Cards 1.5, COM widget provider model).

## License

This project is available under the [MIT License](LICENSE).

# Widget size and interaction checks

The card is built around one question: when can I leave? The estimated finish uses the Title style (28px bold), with the time left beside it and a progress bar below. The bar is a 536×12 PNG data URI drawn by the provider (`ShiftPresentation.BuildProgressBar`) at 2× for the 268px content width, in the Windows accent color, or green once the finish time is reached. Widgets host configs have no container background styles, so an image is the only way to draw it.

The card shows one of five states, chosen by `ShiftPresentation.GetPhase`:

| State | Headline | Primary action |
| --- | --- | --- |
| Idle | Not clocked in, plus how auto clock-in will start the day | Clock in |
| Working | Estimated finish, time left, progress | Clock out |
| Overtime | Estimated finish in green, time over | Clock out |
| Missing | "When did you start?": auto clock-in is on but no sign-in/unlock record exists | Clock in now |
| Clocked out | Clocked out, with start, end and total | Undo clock-out (until the next work cycle) |

Touch targets follow Microsoft's [widget interaction guidance](https://learn.microsoft.com/en-us/windows/apps/design/widgets/widgets-interaction-design): at most 1 on Small, 3 on Medium and 4 on Large.

- **Small** (82px of content): the finish time or headline, one status line, the primary button and the progress bar. Nothing else is tappable.
- **Medium**: date and current time, the hero, the start time with an **Edit** link, the primary button and the last shift.
- **Large**: Medium plus start / worked / left figures, an **Edit start time** button, the last shift and a settings summary.

Settings (workday length, time format, auto clock-in) are in the Widgets Board **⋯ › Customize widget** menu (`IWidgetProvider2.OnCustomizationRequested`, `IsCustomizable="true"`). The customization card uses drop-downs, a toggle and the system time picker; Done and Cancel sit on their own row below the whole form, since Done saves everything at once and Cancel discards. Small has no room for the form and asks the user to resize the widget. Microsoft documents a Widgets Board bug where the ⋯ menu can stop responding after a customization card is shown.

Editing the start time and clear confirmation replace the card with a compact form. Both the start time and the earliest auto clock-in use the system time picker (`Input.Time`), which takes and submits `HH:mm`. The host draws it as a WinUI TimePicker, at least 242px wide, whose 12/24-hour format follows Windows' clock settings; a card cannot choose it, so the picker may differ from the card's own time format setting. Hour and minute drop-downs were tried to follow the card's setting and dropped in favor of the system picker. In the edit form, Save/Cancel sit below the time picker next to the validation message (on Small, the shift date or the validation message). A rejected save keeps the submitted time, with a short validation message. Cancel does not validate inputs. **Clear this shift** is inside the edit form, behind a confirmation.

In 12-hour mode the card shows times with the display language's own pattern and markers (`9:30 AM`, `上午 9:30`) rather than the system culture's, so the setting also works where Windows' short time is 24-hour. Stored and submitted times are `HH:mm`; `TimeInput.TryParse` also accepts other 24-hour forms and explicit AM/PM forms, and every displayed time parses back unchanged.

The edit form shows the original shift's local date. Saving replaces only the time on that date, never inferring yesterday/today from the current clock. A future start or nonexistent daylight-saving time leaves the shift untouched and shows an error. Repeated daylight-saving times retain the original occurrence's offset. Estimated finish is recalculated and old reminder actions are invalidated. Changing the shift date is not part of this time-only editor.

When the clock-in was recorded in a different UTC offset than the current timezone uses for that instant (travel, a VM), Medium and Large show a short note under the times; Small omits it. A daylight-saving change after clock-in does not count.

The regression check uses Microsoft's Adaptive Cards renderer, templating engine and Widgets Board Designer host configuration/CSS. The fixed card dimensions are 300×146, 300×304 and 300×462 CSS pixels, including 48 pixels for attribution and 16 pixels at the bottom. The check renders 15 states across three sizes, three languages and both themes (270 cases) with worst-case text (12-hour times, double-digit durations). It checks vertical bounds, text truncation, input/button widths, that form buttons sit below every field, the touch-target limits, text-link tap widths, the progress bar, and exercises Save/Cancel with an edited time, including every customization field. Time pickers are given the host's 242px minimum width.

On Windows with Node.js, npm and Microsoft Edge, prepare Playwright once:

```powershell
npm install --prefix artifacts/widget-layout-check playwright@1.62.1
./tests/WidgetLayout.Tests.ps1
```

The script downloads pinned Microsoft renderer packages from npm and verifies their published SHA-512 integrity. `-NodePath` and `-PlaywrightModule` accept existing runtime/module locations; `-GalleryLanguage 0`, `1`, or `2` selects English, Traditional Chinese or Simplified Chinese screenshots. Results and screenshots stay under ignored `artifacts/widget-layout-check`.

These browser checks catch layout regressions using the published host settings. The designer CSS paints every button in the accent color; the real host draws non-primary buttons in the neutral style. Final acceptance still requires the installed Windows Widgets host, especially for the progress bar image, the time pickers, the customization card, and non-default accessibility text sizing.

References: [widget sizes and margins](https://learn.microsoft.com/en-us/windows/apps/design/widgets/widgets-design-fundamentals), [widget interaction constraints](https://learn.microsoft.com/en-us/windows/apps/design/widgets/widgets-interaction-design), [widget customization](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/implement-widget-provider-cs#implementing-widget-customization).

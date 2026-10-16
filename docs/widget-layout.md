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

Settings (workday length, time format, auto clock-in) are in the Widgets Board **⋯ › Customize widget** menu (`IWidgetProvider2.OnCustomizationRequested`, `IsCustomizable="true"`). The customization card uses drop-downs, a toggle and a 24-hour time input; Done saves everything at once and Cancel discards. Small has no room for the form and asks the user to resize the widget. Microsoft documents a Widgets Board bug where the ⋯ menu can stop responding after a customization card is shown.

Editing the start time and clear confirmation replace the card with a compact form. Time inputs use an explicit 24-hour label and share a row with Save/Cancel. An invalid value stays visible, with a short validation message (on Small it replaces the label above the input). Cancel does not validate inputs. **Clear this shift** is inside the edit form, behind a confirmation.

Time parsing occurs on Save, using the same rules for clock-in edits and auto clock-in settings. Accepted forms are `9:30`, `09:30` and exactly four digits `0930`; successful saves use canonical `HH:mm` input values. Full-width digits/colon and leading/trailing whitespace are equivalent. Bare hours, three-digit shorthand, AM/PM, seconds and out-of-range times are rejected. Inputs have no character limit that could silently truncate a pasted value such as `09:30 PM` into a different valid time. No live mask modifies the text or cursor while typing.

The edit form shows the original shift's local date. Saving replaces only the time on that date, never inferring yesterday/today from the current clock. A future start or nonexistent daylight-saving time leaves the shift untouched and shows an error. Repeated daylight-saving times retain the original occurrence's offset. Estimated finish is recalculated and old reminder actions are invalidated. Changing the shift date is not part of this time-only editor.

When the clock-in was recorded in a different UTC offset than the current timezone uses for that instant (travel, a VM), Medium and Large show a short note under the times; Small omits it. A daylight-saving change after clock-in does not count.

The regression check uses Microsoft's Adaptive Cards renderer, templating engine and Widgets Board Designer host configuration/CSS. The fixed card dimensions are 300×146, 300×304 and 300×462 CSS pixels, including 48 pixels for attribution and 16 pixels at the bottom. The check renders 15 states across three sizes, three languages and both themes (270 cases) with worst-case text (12-hour times, double-digit durations). It checks vertical bounds, text truncation, input/button widths, the touch-target limits, the progress bar, and exercises Save/Cancel with edited input values, including every customization field.

On Windows with Node.js, npm and Microsoft Edge, prepare Playwright once:

```powershell
npm install --prefix artifacts/widget-layout-check playwright@1.62.1
./tests/WidgetLayout.Tests.ps1
```

The script downloads pinned Microsoft renderer packages from npm and verifies their published SHA-512 integrity. `-NodePath` and `-PlaywrightModule` accept existing runtime/module locations; `-GalleryLanguage 0`, `1`, or `2` selects English, Traditional Chinese or Simplified Chinese screenshots. Results and screenshots stay under ignored `artifacts/widget-layout-check`.

These browser checks catch layout regressions using the published host settings. The designer CSS paints every button in the accent color; the real host draws non-primary buttons in the neutral style. Final acceptance still requires the installed Windows Widgets host, especially for the progress bar image, the customization card, and non-default accessibility text sizing.

References: [widget sizes and margins](https://learn.microsoft.com/en-us/windows/apps/design/widgets/widgets-design-fundamentals), [widget interaction constraints](https://learn.microsoft.com/en-us/windows/apps/design/widgets/widgets-interaction-design), [widget customization](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/implement-widget-provider-cs#implementing-widget-customization).

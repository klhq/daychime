# Widget size and interaction checks

Small is a compact workday control: start/finish times and Clock in/Clock out, with More opening secondary controls. It omits the full date, current clock, long previous-shift summary and explanatory text. Medium and Large add those details within their available space. A `+1` marks a next-day finish on Small/Medium; Large uses a separate next-day caption so a 12-hour time cannot be truncated. When the clock-in was recorded in a different UTC offset than the current timezone uses for that instant (travel, a VM), Medium and Large show a short note under the times; Small omits it. A daylight-saving change after clock-in does not count.

All sizes use dedicated edit, auto clock-in and clear-confirmation modes. Normal workday content is absent from these modes. Time inputs use an explicit 24-hour `HH:mm` label and share a row with Save/Cancel. An invalid value stays visible, with a short validation message below it. Cancel does not validate inputs. Auto settings hide the disable link while showing an error, reserving space for feedback.

Time parsing occurs on Save, using the same rules for clock-in edits and auto clock-in settings. Accepted forms are `9:30`, `09:30` and exactly four digits `0930`; successful saves use canonical `HH:mm` input values. Full-width digits/colon and leading/trailing whitespace are equivalent. Bare hours, three-digit shorthand, AM/PM, seconds and out-of-range times are rejected. Inputs have no character limit that could silently truncate a pasted value such as `09:30 PM` into a different valid time. No live mask modifies the text or cursor while typing.

The clock-in edit label displays the original shift's local date (`yyyy-MM-dd`) and 24-hour format. Saving replaces only the time on that displayed date, never inferring yesterday/today from the current clock. A future start or nonexistent daylight-saving time leaves the shift untouched and shows an error. Repeated daylight-saving times retain the original occurrence's offset. Estimated finish is recalculated and old reminder actions are invalidated. Changing the shift date is not part of this time-only editor.

More contains workday length, time format and auto clock-in controls, plus Edit/Clear or Undo as appropriate. New shift is available after the current estimated finish. Changing workday length keeps More open. Done returns to the main view.

The regression check uses Microsoft's Adaptive Cards renderer, templating engine and Widgets Board Designer host configuration/CSS. The fixed card dimensions are 300×146, 300×304 and 300×462 CSS pixels, including 48 pixels for attribution and 16 pixels at the bottom. Small therefore has only 82 pixels of content height. The check renders 17 states across three sizes, three languages and both themes (306 cases), including a timezone-adjusted shift, a missed current cycle with an older unfinished shift and format/future/DST edit errors. It checks vertical bounds and text/input/button widths and exercises Save/Cancel with edited input values.

On Windows with Node.js, npm and Microsoft Edge, prepare Playwright once:

```powershell
npm install --prefix artifacts/widget-layout-check playwright@1.62.1
./tests/WidgetLayout.Tests.ps1
```

The script downloads pinned Microsoft renderer packages from npm and verifies their published SHA-512 integrity. `-NodePath` and `-PlaywrightModule` accept existing runtime/module locations; `-GalleryLanguage 0`, `1`, or `2` selects English, Traditional Chinese or Simplified Chinese screenshots. Results and screenshots stay under ignored `artifacts/widget-layout-check`.

These browser checks catch layout regressions using the published host settings. Final acceptance still requires the installed Windows Widgets host, especially with non-default accessibility text sizing.

References: [widget sizes and margins](https://learn.microsoft.com/en-us/windows/apps/design/widgets/widgets-design-fundamentals), [widget interaction constraints](https://learn.microsoft.com/en-us/windows/apps/design/widgets/widgets-interaction-design).

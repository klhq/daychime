# Workday Widget

An English-language Windows workday tracker.

> A minimal Windows 11 workday tracker for recording a clock-in time and seeing the expected finish time at a glance.

## Current behavior

- Uses a configurable workday length (default 9 hours from clock-in to expected finish).
- Records today's clock-in time.
- Calculates the expected finish time.
- Lets the user edit the clock-in time or clear today's record.
- Keeps a local CSV history.

## Target experience

The planned primary interface is a native Windows 11 Widgets Board card, opened with `Win + W`:

```text
Mon, Sep 21
Now 14:32                            24h

Clock in                    09:07
Expected finish             18:07

Clear today's record

[ Edit time ]
```

The footer never carries more than two buttons at once. `Clear` lives as a
small always-visible link under the stats instead of a footer button, and the
12/24-hour preference is a one-tap chip next to "Now" instead of a separate
settings screen — it never interrupts the clock-in flow.

At the expected finish time, the tool sends one notification only. It never automatically clocks out or clears a record. On workstation unlock, it refreshes the current day and sends a missed finish-time notification once when needed.

An optional "Auto clock in on unlock" preference clocks the user in automatically the first time they unlock their PC each day, so the widget never needs to be opened at all on a normal day. It's off by default; a small accent-colored line under the "Not clocked in yet" hint toggles it with one tap (the same low-friction pattern as the 12/24-hour chip) instead of adding a settings screen. Once a clock-in exists for the day — whether automatic or manual — unlocking again does nothing, and it never overwrites an edited or already-recorded time.

The workday length itself is also configurable: on medium and large sizes, a small accent-colored "9-hour workday" line cycles through a handful of presets (8, 8.5, 9, 9.5, 10 hours) with one tap, the same pattern as the other in-body toggles. Clock-in and finish times are stored and computed against the machine's current local timezone, so a clock-in recorded before a timezone change (travel, a VM) still displays and fires its reminder correctly — with a small note appearing only when that adjustment actually happened.

## Repository contents

- `outputs/WorkdayWidget/WorkdayWidget.ps1` - current local prototype.
- `outputs/WorkdayWidget/WorkdayWidget.cs` - prior native-window prototype source.

The next implementation replaces the prototype with a packaged Windows Widget Provider, using the Windows App SDK and Adaptive Cards.

## License

This project is available under the [MIT License](LICENSE).

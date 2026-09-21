# Workday Widget

An English-language Windows workday tracker.

> A minimal Windows 11 workday tracker for recording a clock-in time and seeing the expected finish time at a glance.

## Current behavior

- Uses an 8-hour workday plus a 1-hour break.
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

## Repository contents

- `outputs/WorkdayWidget/WorkdayWidget.ps1` - current local prototype.
- `outputs/WorkdayWidget/WorkdayWidget.cs` - prior native-window prototype source.

The next implementation replaces the prototype with a packaged Windows Widget Provider, using the Windows App SDK and Adaptive Cards.

## License

This project is available under the [MIT License](LICENSE).

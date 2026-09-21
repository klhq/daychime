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

Clock in                    09:07
Expected finish             18:07

[ Edit time ]   [ Clear ]
```

At the expected finish time, the tool sends one notification only. It never automatically clocks out or clears a record. On workstation unlock, it refreshes the current day and sends a missed finish-time notification once when needed.

## Repository contents

- `outputs/WorkdayWidget/WorkdayWidget.ps1` - current local prototype.
- `outputs/WorkdayWidget/WorkdayWidget.cs` - prior native-window prototype source.

The next implementation replaces the prototype with a packaged Windows Widget Provider, using the Windows App SDK and Adaptive Cards.

## License

This project is available under the [MIT License](LICENSE).

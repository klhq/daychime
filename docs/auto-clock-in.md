# Auto clock-in and missed session events

The widget provider is started on demand by Windows. It may not be running when a user starts work. Board activation is therefore never evidence of a clock-in time.

On a live sign-in/unlock or board activation, Daychime looks for eligible activity in the current work cycle. It reads session logon/reconnect events 21/25 from `Microsoft-Windows-TerminalServices-LocalSessionManager/Operational`, and unlock events 4801 from `Security` when readable. Events must match the current account and Windows session. It chooses the earliest eligible timestamp, respecting an existing active shift, the handled cycle, and the time auto clock-in was enabled. A live sign-in/unlock can supply the event time if history is unavailable.

No permission elevation or audit-policy changes are requested. Security auditing is often unavailable to regular users; if Windows did not retain an accessible event, Daychime cannot reconstruct a missed unlock. It leaves the record unchanged and shows a manual-entry hint. This is best-effort recovery, not a guaranteed background attendance service. Finish reminders cannot be scheduled while the provider has never started; already-passed finish times are not replayed.

Turning auto clock-in on does not backfill activity from before it was enabled. Existing installations with auto clock-in already on can recover the current cycle without an enable timestamp. Existing clock-ins, including an incorrectly recorded opening time from an older version, are not silently rewritten: correct those in Edit time.

Auto settings use a separate card body for all sizes. The 24-hour time input, Save and Cancel share one row, and the expanded explanation is limited to Large. Previous-shift summaries and the normal workday body are hidden while settings are open. See [widget layout](widget-layout.md) for size and validation coverage.

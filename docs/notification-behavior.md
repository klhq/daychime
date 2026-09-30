# Finish notification behavior

The **Clock out now** button records the time the app handles the click. It only applies to the widget and shift revision named in that notification. The estimated finish never automatically becomes an actual clock-out.

| Situation | Expected behavior | Verification |
| --- | --- | --- |
| Click after an overnight shift | Record the actual time, keep the completed shift in the recent summary | Lifecycle tests |
| Click twice | Preserve the first clock-out | Lifecycle tests |
| A new shift has already started | Ignore the previous shift's notification | Lifecycle tests |
| Edit the start or working hours, including changing them back | Invalidate the old notification revision | Lifecycle tests and revision persistence |
| Undo a clock-out, then receive a delayed repeat click | Ignore the old notification; the widget can still clock out again | Lifecycle tests |
| Clear a shift or remove the widget | Cancel pending reminders and remove their notifications from history | Schedule/history reconciliation; physical unpin still needs UI testing |
| Widget was removed but its deletion callback has not arrived | Check the host's widget list before writing | Host existence check |
| Notification arrives just as the user changes time format or opens the widget | Preserve the matching scheduled reminder, including one whose delivery time has just passed | Schedule reconciliation; timing race still needs live testing |
| Program is stopped | Windows starts the registered notification handler and restores the widget state | Native COM activation smoke test |
| Click an invalid or outdated notification | Leave the current record unchanged | Lifecycle tests and native callback smoke test |
| System clock is moved before the clock-in time | Do not record a negative elapsed time | Lifecycle tests |
| Timezone changes | Keep the same shift identity for the same instant | Lifecycle tests |
| Unfinished shift reaches estimated finish plus 24 hours | Expire it; ignore its notification | Lifecycle tests, including the exact boundary |
| Widget update fails during notification clock-out | Restore in-memory state and return an activation error so the action is retryable | Error handling; host-failure injection not yet tested |

State reads accept both the original three-field shift format and the new fourth field containing the notification revision. New shifts, edits and undo generate a fresh revision. The revision is persisted alongside the shift so restarting the provider does not revive invalidated notifications.

Run the lifecycle checks with `dotnet run --project tests/ShiftLifecycle.Tests.csproj --configuration Release`. After installing a local package, run `./tests/NotificationActivation.Smoke.ps1`; optionally supply `-WidgetId` to exercise rejection on an existing widget with an invalid revision. The smoke test never clocks out a real shift.

Before submitting a release, physically click a scheduled notification with the provider running and stopped, then confirm the actual time in the widget. Also test undo, unpinning, and an edit immediately before the reminder time. A native callback test verifies activation plumbing, not the complete Windows notification UI interaction.

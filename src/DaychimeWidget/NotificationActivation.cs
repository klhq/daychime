using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WidgetHelper.Com;

namespace DaychimeWidget;

// Native desktop toast callback: no extra notification runtime is required.
[ComVisible(true)]
[Guid("53E31837-6600-4A81-9395-75CFFE746F94")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface INotificationActivationCallback
{
    void Activate([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs, IntPtr data, uint count);
}

[ComVisible(true)]
[Guid("7C68559D-9F68-4204-AB41-65E463F76304")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class NotificationActivation : INotificationActivationCallback
{
    public void Activate(string appUserModelId, string invokedArgs, IntPtr data, uint count)
    {
        var clickedAt = DateTimeOffset.Now;
        try
        {
            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in (invokedArgs ?? string.Empty).Split('&'))
            {
                var separator = pair.IndexOf('=');
                if (separator > 0)
                    args[Uri.UnescapeDataString(pair[..separator])] = Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
            if (args.TryGetValue("action", out var action) && action == "clockOut"
                && args.TryGetValue("widget", out var widgetId) && args.TryGetValue("shift", out var reminderKey))
                WidgetProvider.ClockOutFromNotification(widgetId, reminderKey, clickedAt);
            else ProviderDiagnostics.Write("Notification body clicked; no clock-out requested.");
        }
        catch (Exception ex)
        {
            ProviderDiagnostics.Write($"Notification activation failed: {ex}");
            // Return failure to Windows so the user can retry the action.
            throw;
        }
    }

    internal static IDisposable Register() => new Registration();

    private sealed class Registration : IDisposable
    {
        private readonly Factory factory = new();
        private readonly uint cookie;
        public Registration()
        {
            ClassObject.Register(typeof(NotificationActivation).GUID, factory, out cookie);
            ProviderDiagnostics.Write("Notification COM activator registered.");
        }
        public void Dispose()
        {
            ClassObject.Revoke(cookie);
            GC.KeepAlive(factory);
        }
    }

    private sealed class Factory : IClassFactory
    {
        public int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr result)
        {
            result = IntPtr.Zero;
            if (outer != IntPtr.Zero) return unchecked((int)0x80040110);
            var unknown = Marshal.GetIUnknownForObject(new NotificationActivation());
            try { return Marshal.QueryInterface(unknown, ref iid, out result); }
            finally { Marshal.Release(unknown); }
        }
        public int LockServer(bool locked) => 0;
    }
}

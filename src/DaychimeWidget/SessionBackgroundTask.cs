using System;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.ApplicationModel.Background;
using WidgetHelper.Com;
using WinRT;

namespace DaychimeWidget;

// Windows starts the provider on sign-in/unlock, so auto clock-in and the finish
// reminder no longer wait for the Widgets Board to be opened.
[ComVisible(true)]
[Guid("3F6B2C1E-8D4A-4E7B-9C21-5A7D0E9B4F63")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class SessionBackgroundTask : IBackgroundTask
{
    // UserPresent is the documented unlock trigger; SessionConnected covers sign-in and reconnect.
    private static readonly (string Name, SystemTriggerType Trigger)[] Registrations = {
        ("Daychime.UserPresent", SystemTriggerType.UserPresent),
        ("Daychime.SessionConnected", SystemTriggerType.SessionConnected)
    };

    internal static ManualResetEvent Completed { get; } = new(false);

    public void Run(IBackgroundTaskInstance taskInstance)
    {
        try
        {
            ProviderDiagnostics.Write($"Background task {taskInstance.Task.Name} started.");
            WidgetProvider.HandleSessionUnlock();
        }
        catch (Exception ex) { ProviderDiagnostics.Write($"Background task failed: {ex}"); }
        finally { Completed.Set(); }
    }

    internal static void EnsureRegistered()
    {
        // COM entry points for background tasks need Windows 10 2004; widgets need Windows 11 anyway.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        try
        {
            var access = BackgroundExecutionManager.RequestAccessAsync().AsTask().GetAwaiter().GetResult();
            if (access is BackgroundAccessStatus.DeniedByUser or BackgroundAccessStatus.DeniedBySystemPolicy or BackgroundAccessStatus.Unspecified)
            {
                ProviderDiagnostics.Write($"Background tasks unavailable: {access}.");
                return;
            }
            foreach (var (name, trigger) in Registrations)
            {
                var registered = false;
                foreach (var existing in BackgroundTaskRegistration.AllTasks.Values)
                    registered |= existing.Name == name;
                if (registered) continue;
                var builder = new BackgroundTaskBuilder { Name = name };
                builder.SetTrigger(new SystemTrigger(trigger, false));
                builder.SetTaskEntryPointClsid(typeof(SessionBackgroundTask).GUID);
                builder.Register();
                ProviderDiagnostics.Write($"Background task {name} registered ({access}).");
            }
        }
        catch (Exception ex) { ProviderDiagnostics.Write($"Background task registration failed: {ex}"); }
    }

    internal static IDisposable Register() => new Registration();

    private sealed class Registration : IDisposable
    {
        private readonly Factory factory = new();
        private readonly uint cookie;
        public Registration() => ClassObject.Register(typeof(SessionBackgroundTask).GUID, factory, out cookie);
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
            var inspectable = MarshalInspectable<IBackgroundTask>.FromManaged(new SessionBackgroundTask());
            try { return Marshal.QueryInterface(inspectable, ref iid, out result); }
            finally { Marshal.Release(inspectable); }
        }
        public int LockServer(bool locked) => 0;
    }
}

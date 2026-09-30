# Run after installing the local package. Exercises native activation without changing a real shift.
param([string]$WidgetId)
$ErrorActionPreference = 'Stop'
if (-not ('DaychimeToastProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDaychimeToastProbe {
    void Activate([MarshalAs(UnmanagedType.LPWStr)] string app,
        [MarshalAs(UnmanagedType.LPWStr)] string args, IntPtr data, uint count);
}
public static class DaychimeToastProbe {
    public static void Invoke(string arguments) {
        var type = Type.GetTypeFromCLSID(new Guid("7C68559D-9F68-4204-AB41-65E463F76304"));
        var instance = Activator.CreateInstance(type);
        try { ((IDaychimeToastProbe)instance).Activate("klhq.DaymarkWidget_3ypt615rycnmw!App", arguments, IntPtr.Zero, 0); }
        finally { Marshal.ReleaseComObject(instance); }
    }
}
'@
}
[DaychimeToastProbe]::Invoke('action=view')
[DaychimeToastProbe]::Invoke('action=clockOut&widget=smoke-test-nonexistent-widget&shift=invalid')
if ($WidgetId) {
    [DaychimeToastProbe]::Invoke("action=clockOut&widget=$([Uri]::EscapeDataString($WidgetId))&shift=invalid")
}
Write-Output 'Native notification body and clock-out callbacks succeeded (no real shift changed).'

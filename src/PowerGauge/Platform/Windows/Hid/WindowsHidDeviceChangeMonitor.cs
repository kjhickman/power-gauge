using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Platform.Windows.Hid;

public sealed partial class WindowsHidDeviceChangeMonitor : IDeviceChangeMonitor
{
    private const int FilterTypeDeviceInterface = 0;
    private const int ActionDeviceInterfaceArrival = 0;
    private const int ActionDeviceInterfaceRemoval = 1;
    private const int CrSuccess = 0;

    private IntPtr _notificationHandle;
    private GCHandle _selfHandle;
    private int _disposed;

    public WindowsHidDeviceChangeMonitor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WindowsHidDeviceEnumerator.HidD_GetHidGuid(out var hidGuid);
        var filter = new CmNotifyFilter
        {
            cbSize = (uint)Unsafe.SizeOf<CmNotifyFilter>(),
            FilterType = FilterTypeDeviceInterface,
            ClassGuid = hidGuid,
        };

        _selfHandle = GCHandle.Alloc(this, GCHandleType.Weak);
        int result;
        unsafe
        {
            result = CM_Register_Notification(
                in filter,
                GCHandle.ToIntPtr(_selfHandle),
                &OnDeviceNotification,
                out _notificationHandle);
        }

        if (result != CrSuccess)
        {
            _notificationHandle = IntPtr.Zero;
            _selfHandle.Free();
        }
    }

    public event Action? DevicesChanged;

    internal bool IsRegistered => _notificationHandle != IntPtr.Zero;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var handle = Interlocked.Exchange(ref _notificationHandle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            _ = CM_Unregister_Notification(handle);
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private void RaiseDevicesChanged()
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            DevicesChanged?.Invoke();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint OnDeviceNotification(
        IntPtr hNotify,
        IntPtr context,
        int action,
        IntPtr eventData,
        uint eventDataSize)
    {
        if (action is ActionDeviceInterfaceArrival or ActionDeviceInterfaceRemoval && context != IntPtr.Zero)
        {
            var gcHandle = GCHandle.FromIntPtr(context);
            if (gcHandle.IsAllocated && gcHandle.Target is WindowsHidDeviceChangeMonitor monitor)
            {
                monitor.RaiseDevicesChanged();
            }
        }

        return CrSuccess;
    }

    [LibraryImport("cfgmgr32.dll")]
    private static unsafe partial int CM_Register_Notification(
        in CmNotifyFilter filter,
        IntPtr context,
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr, uint, uint> callback,
        out IntPtr notifyContext);

    [LibraryImport("cfgmgr32.dll")]
    private static partial int CM_Unregister_Notification(IntPtr notifyContext);

    [InlineArray(384)]
    private struct UnionTailPadding
    {
        private byte _element0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CmNotifyFilter
    {
        public uint cbSize;
        public uint Flags;
        public int FilterType;
        public uint Reserved;
        public Guid ClassGuid;
        public UnionTailPadding Padding;
    }
}

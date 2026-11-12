using System;
using System.Runtime.InteropServices;
using System.Threading;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Platform.MacOS.Hid;

public sealed partial class MacOsHidDeviceChangeMonitor : IDeviceChangeMonitor
{
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string IOKitLibrary = "/System/Library/Frameworks/IOKit.framework/IOKit";

    private IntPtr _manager;
    private IntPtr _runLoop;
    private MacOsHidInterop.CfStringHandle _runLoopMode;
    private GCHandle _selfHandle;
    private int _disposed;

    public MacOsHidDeviceChangeMonitor()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        _manager = IOHIDManagerCreate(IntPtr.Zero, 0);
        if (_manager == IntPtr.Zero)
        {
            return;
        }

        _runLoop = CFRunLoopGetMain();
        _runLoopMode = new MacOsHidInterop.CfStringHandle("kCFRunLoopDefaultMode");
        if (_runLoop == IntPtr.Zero || _runLoopMode.Handle == IntPtr.Zero)
        {
            _runLoopMode.Dispose();
            MacOsHidInterop.Release(_manager);
            _manager = IntPtr.Zero;
            return;
        }

        _selfHandle = GCHandle.Alloc(this, GCHandleType.Weak);
        var context = GCHandle.ToIntPtr(_selfHandle);

        IOHIDManagerSetDeviceMatching(_manager, IntPtr.Zero);
        unsafe
        {
            IOHIDManagerRegisterDeviceMatchingCallback(_manager, &OnHidDeviceChanged, context);
            IOHIDManagerRegisterDeviceRemovalCallback(_manager, &OnHidDeviceChanged, context);
        }

        IOHIDManagerScheduleWithRunLoop(_manager, _runLoop, _runLoopMode.Handle);
    }

    public event Action? DevicesChanged;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var manager = Interlocked.Exchange(ref _manager, IntPtr.Zero);
        if (manager != IntPtr.Zero)
        {
            unsafe
            {
                IOHIDManagerRegisterDeviceMatchingCallback(manager, null, IntPtr.Zero);
                IOHIDManagerRegisterDeviceRemovalCallback(manager, null, IntPtr.Zero);
            }

            if (_runLoop != IntPtr.Zero && _runLoopMode.Handle != IntPtr.Zero)
            {
                IOHIDManagerUnscheduleFromRunLoop(manager, _runLoop, _runLoopMode.Handle);
            }

            MacOsHidInterop.Release(manager);
        }

        _runLoopMode.Dispose();

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

    [UnmanagedCallersOnly]
    private static void OnHidDeviceChanged(IntPtr context, int result, IntPtr sender, IntPtr device)
    {
        if (context == IntPtr.Zero)
        {
            return;
        }

        var gcHandle = GCHandle.FromIntPtr(context);
        if (gcHandle.IsAllocated && gcHandle.Target is MacOsHidDeviceChangeMonitor monitor)
        {
            monitor.RaiseDevicesChanged();
        }
    }

    [LibraryImport(IOKitLibrary)]
    private static partial IntPtr IOHIDManagerCreate(IntPtr allocator, uint options);

    [LibraryImport(IOKitLibrary)]
    private static partial void IOHIDManagerSetDeviceMatching(IntPtr manager, IntPtr matching);

    [LibraryImport(IOKitLibrary)]
    private static unsafe partial void IOHIDManagerRegisterDeviceMatchingCallback(
        IntPtr manager,
        delegate* unmanaged<IntPtr, int, IntPtr, IntPtr, void> callback,
        IntPtr context);

    [LibraryImport(IOKitLibrary)]
    private static unsafe partial void IOHIDManagerRegisterDeviceRemovalCallback(
        IntPtr manager,
        delegate* unmanaged<IntPtr, int, IntPtr, IntPtr, void> callback,
        IntPtr context);

    [LibraryImport(IOKitLibrary)]
    private static partial void IOHIDManagerScheduleWithRunLoop(IntPtr manager, IntPtr runLoop, IntPtr runLoopMode);

    [LibraryImport(IOKitLibrary)]
    private static partial void IOHIDManagerUnscheduleFromRunLoop(IntPtr manager, IntPtr runLoop, IntPtr runLoopMode);

    [LibraryImport(CoreFoundationLibrary)]
    private static partial IntPtr CFRunLoopGetMain();
}

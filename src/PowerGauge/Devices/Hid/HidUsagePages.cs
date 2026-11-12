using System;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Devices.Hid;

public static class HidUsagePages
{
    public const int GenericDesktop = 0x0001;
    public const int MouseUsage = 0x0002;
    public const int KeyboardUsage = 0x0006;
    public const int KeypadUsage = 0x0007;

    public static bool IsKeyboard(HidDeviceInfo device)
    {
        if (device.UsagePage == GenericDesktop && device.Usage is KeyboardUsage or KeypadUsage)
        {
            return true;
        }

        return device.DevicePath.Contains("\\kbd", StringComparison.OrdinalIgnoreCase);
    }
}

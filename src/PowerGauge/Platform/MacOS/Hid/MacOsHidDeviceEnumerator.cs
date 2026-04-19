using System;
using System.Collections.Generic;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Platform.MacOS.Hid;

public sealed class MacOsHidDeviceEnumerator : IHidDeviceEnumerator
{
    public IReadOnlyList<HidDeviceInfo> Enumerate()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return [];
        }

        if (!MacOsHidInterop.TryCopyDevices(out var manager, out var deviceSet, out var devices, out var error))
        {
            throw new InvalidOperationException(error);
        }

        try
        {
            var hidDevices = new List<HidDeviceInfo>();

            foreach (var device in devices)
            {
                var registryEntryId = MacOsHidInterop.GetRegistryEntryId(device);
                if (registryEntryId == 0)
                {
                    continue;
                }

                hidDevices.Add(new HidDeviceInfo(
                    MacOsHidInterop.FormatRegistryEntryId(registryEntryId),
                    MacOsHidInterop.GetIntProperty(device, "VendorID"),
                    MacOsHidInterop.GetIntProperty(device, "ProductID"),
                    MacOsHidInterop.GetIntProperty(device, "MaxFeatureReportSize"),
                    GetProductName(device)));
            }

            return hidDevices;
        }
        finally
        {
            MacOsHidInterop.Release(deviceSet);
            MacOsHidInterop.Release(manager);
        }
    }

    private static string GetProductName(IntPtr device)
    {
        var productName = MacOsHidInterop.GetStringProperty(device, "Product");
        return string.IsNullOrWhiteSpace(productName)
            ? "Unknown HID device"
            : productName;
    }
}

namespace ViperLink.App.Services;

internal sealed record WindowsHidDeviceInfo(
    string DevicePath,
    int VendorId,
    int ProductId,
    int FeatureReportLength,
    string ProductName);

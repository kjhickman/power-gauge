namespace PowerGauge.Platform.Abstractions;

public sealed record HidDeviceInfo(
    string DevicePath,
    int VendorId,
    int ProductId,
    int FeatureReportLength,
    string ProductName);

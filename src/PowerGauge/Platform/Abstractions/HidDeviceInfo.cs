namespace PowerGauge.Platform.Abstractions;

public sealed record HidDeviceInfo(
    string DevicePath,
    int VendorId,
    int ProductId,
    int FeatureReportLength,
    string ProductName,
    int UsagePage = 0,
    int Usage = 0,
    int InputReportLength = 0,
    int OutputReportLength = 0);

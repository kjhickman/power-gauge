using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Devices.Razer;

internal sealed record RazerPowerPayload(byte[] Bytes);

internal sealed record RazerCommandTimeout(byte[] Bytes);

internal sealed record RazerReadFailure(PowerFailureKind Kind);

internal union RazerReadResult(RazerPowerPayload, RazerCommandTimeout, RazerReadFailure);

internal static class RazerProtocol
{
    public const int VendorId = 0x1532;
    public const int ReportLength = 90;
    public const byte PowerCommandClass = 0x07;
    public const byte GetBatteryCommandId = 0x80;
    public const byte GetChargingStatusCommandId = 0x84;

    public static byte[] BuildRequest(int reportLength, byte transactionId, byte commandClass, byte commandId, byte dataSize = 0x02)
    {
        var request = new byte[reportLength];
        request[0] = 0x00;
        request[1] = transactionId;
        request[5] = dataSize;
        request[6] = commandClass;
        request[7] = commandId;
        request[88] = CalculateChecksum(request);
        return request;
    }

    public static bool IsSuccessResponse(IReadOnlyList<byte> response, byte transactionId, byte commandClass, byte commandId)
    {
        return MatchesHeader(response, transactionId, commandClass, commandId)
            && response[0] is 0x00 or 0x02;
    }

    public static bool IsTimeoutResponse(IReadOnlyList<byte> response, byte transactionId, byte commandClass, byte commandId)
    {
        return MatchesHeader(response, transactionId, commandClass, commandId)
            && response[0] == 0x04;
    }

    public static HidFeatureResponseStatus ClassifyFeatureResponse(ReadOnlySpan<byte> request, ReadOnlySpan<byte> response)
    {
        var offset = request.Length == ReportLength + 1 ? 1 : 0;
        if (request.Length != response.Length || response.Length < offset + 10)
        {
            return HidFeatureResponseStatus.Accepted;
        }

        if (response[offset + 6] != request[offset + 6]
            || response[offset + 7] != request[offset + 7])
        {
            return HidFeatureResponseStatus.UnrelatedCommand;
        }

        var status = response[offset];
        if (status == 0x01)
        {
            return HidFeatureResponseStatus.Pending;
        }

        if (status == 0x00
            && response[offset + 8] == 0x00
            && response[offset + 9] == 0x00)
        {
            return HidFeatureResponseStatus.Pending;
        }

        return HidFeatureResponseStatus.Accepted;
    }

    public static RazerReadResult ReadPowerResponse(
        HidDeviceInfo device,
        IHidDeviceSession session,
        ReadOnlySpan<byte> candidateTransactionIds,
        StringBuilder diagnostics,
        byte commandId,
        string responseLabel)
    {
        var reportLength = Math.Max(ReportLength, device.FeatureReportLength);
        diagnostics.AppendLine($"Feature report length: {reportLength}");
        if (reportLength < ReportLength)
        {
            diagnostics.AppendLine("Skipped: feature report length is shorter than 90 bytes.");
            return new RazerReadFailure(PowerFailureKind.UnsupportedResponse);
        }

        diagnostics.AppendLine("Using native HID feature session.");
        var payloadOffset = reportLength == ReportLength + 1 ? 1 : 0;
        var payloadLength = reportLength - payloadOffset;
        byte[]? timeoutPayload = null;
        var sawSleepingPlaceholder = false;
        var sawTransportTimeout = false;

        foreach (var transactionId in candidateTransactionIds)
        {
            var requestPayload = BuildRequest(payloadLength, transactionId, PowerCommandClass, commandId);
            var request = new byte[reportLength];
            Array.Copy(requestPayload, 0, request, payloadOffset, requestPayload.Length);

            var response = new byte[reportLength];
            if (!session.TryExchangeFeatureReport(request, response, ClassifyFeatureResponse, out var exchangeFailure, out var error))
            {
                diagnostics.AppendLine($"Transaction 0x{transactionId:x2} failed: {error}");
                if (exchangeFailure == PowerFailureKind.DeviceSleeping)
                {
                    sawSleepingPlaceholder = true;
                }
                else if (exchangeFailure == PowerFailureKind.ProtocolTimeout)
                {
                    sawTransportTimeout = true;
                }

                continue;
            }

            var payload = response.AsSpan(payloadOffset, payloadLength).ToArray();
            diagnostics.AppendLine($"{responseLabel} transaction 0x{transactionId:x2} response: {FormatReport(payload)}");

            if (IsSuccessResponse(payload, transactionId, PowerCommandClass, commandId))
            {
                return new RazerPowerPayload(payload);
            }

            if (IsTimeoutResponse(payload, transactionId, PowerCommandClass, commandId))
            {
                timeoutPayload ??= payload;
            }
        }

        if (timeoutPayload is not null)
        {
            return new RazerCommandTimeout(timeoutPayload);
        }

        if (sawSleepingPlaceholder)
        {
            return new RazerReadFailure(PowerFailureKind.DeviceSleeping);
        }

        if (sawTransportTimeout)
        {
            return new RazerReadFailure(PowerFailureKind.ProtocolTimeout);
        }

        return new RazerReadFailure(PowerFailureKind.UnsupportedResponse);
    }

    public static PowerFailureKind MapFailureKind(RazerReadResult result, bool isWirelessDevice)
    {
        return result switch
        {
            RazerCommandTimeout => isWirelessDevice ? PowerFailureKind.DeviceSleeping : PowerFailureKind.ProtocolTimeout,
            RazerReadFailure(var kind) => kind,
            RazerPowerPayload => PowerFailureKind.None,
            null => PowerFailureKind.UnsupportedResponse,
        };
    }

    public static int ParseBatteryPercent(IReadOnlyList<byte> response)
    {
        return (int)Math.Round(response[9] * 100.0 / 255.0, MidpointRounding.AwayFromZero);
    }

    public static bool ParseChargingStatus(IReadOnlyList<byte> response)
    {
        return response[9] == 0x01 || response[11] == 0x01;
    }

    public static string FormatReport(IReadOnlyList<byte> report)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < report.Count && i < ReportLength; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(report[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static bool MatchesHeader(IReadOnlyList<byte> response, byte transactionId, byte commandClass, byte commandId)
    {
        return response.Count >= ReportLength
            && response[1] == transactionId
            && response[6] == commandClass
            && response[7] == commandId;
    }

    private static byte CalculateChecksum(IReadOnlyList<byte> report)
    {
        byte checksum = 0x00;
        for (var index = 2; index <= 87; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }
}

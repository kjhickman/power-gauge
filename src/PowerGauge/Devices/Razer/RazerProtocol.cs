using System;
using System.Text;
using PowerGauge.Domain;
using PowerGauge.Platform.Abstractions;

namespace PowerGauge.Devices.Razer;

internal readonly record struct RazerPowerPayload(byte PrimaryArg, byte TertiaryArg);

internal readonly record struct RazerCommandTimeout;

internal readonly record struct RazerReadFailure(PowerFailureKind Kind);

internal union RazerReadResult(RazerPowerPayload, RazerCommandTimeout, RazerReadFailure);

internal static class RazerProtocol
{
    public const int VendorId = 0x1532;
    public const int ReportLength = 90;
    public const byte PowerCommandClass = 0x07;
    public const byte GetBatteryCommandId = 0x80;
    public const byte GetChargingStatusCommandId = 0x84;

    public static void WriteRequest(Span<byte> request, byte transactionId, byte commandClass, byte commandId, byte dataSize = 0x02)
    {
        request.Clear();
        request[0] = 0x00;
        request[1] = transactionId;
        request[5] = dataSize;
        request[6] = commandClass;
        request[7] = commandId;
        request[88] = CalculateChecksum(request);
    }

    public static bool IsSuccessResponse(ReadOnlySpan<byte> response, byte transactionId, byte commandClass, byte commandId)
    {
        return MatchesHeader(response, transactionId, commandClass, commandId)
            && response[0] is 0x00 or 0x02;
    }

    public static bool IsTimeoutResponse(ReadOnlySpan<byte> response, byte transactionId, byte commandClass, byte commandId)
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
        diagnostics.Append("Feature report length: ").Append(reportLength).AppendLine();
        diagnostics.AppendLine("Using native HID feature session.");

        var payloadOffset = reportLength == ReportLength + 1 ? 1 : 0;
        var payloadLength = reportLength - payloadOffset;
        var sawCommandTimeout = false;
        var sawSleepingPlaceholder = false;
        var sawTransportTimeout = false;

        Span<byte> request = reportLength <= 256 ? stackalloc byte[reportLength] : new byte[reportLength];
        Span<byte> response = reportLength <= 256 ? stackalloc byte[reportLength] : new byte[reportLength];

        foreach (var transactionId in candidateTransactionIds)
        {
            request.Clear();
            WriteRequest(request.Slice(payloadOffset, payloadLength), transactionId, PowerCommandClass, commandId);

            response.Clear();
            if (!session.TryExchangeFeatureReport(request, response, ClassifyFeatureResponse, out var exchangeFailure, out var error))
            {
                diagnostics.Append("Transaction 0x").Append(transactionId.ToString("x2")).Append(" failed: ").AppendLine(error);
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

            var payload = response.Slice(payloadOffset, payloadLength);
            diagnostics.Append(responseLabel).Append(" transaction 0x");
            AppendByteHex(diagnostics, transactionId);
            diagnostics.Append(" response: ");
            AppendReportHex(diagnostics, payload);
            diagnostics.AppendLine();

            if (IsSuccessResponse(payload, transactionId, PowerCommandClass, commandId))
            {
                return new RazerPowerPayload(payload[9], payload[11]);
            }

            if (IsTimeoutResponse(payload, transactionId, PowerCommandClass, commandId))
            {
                sawCommandTimeout = true;
            }
        }

        if (sawCommandTimeout)
        {
            return new RazerCommandTimeout();
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

    public static int ParseBatteryPercent(byte rawBatteryByte)
    {
        return (int)Math.Round(rawBatteryByte * 100.0 / 255.0, MidpointRounding.AwayFromZero);
    }

    public static bool ParseChargingStatus(RazerPowerPayload payload)
    {
        return payload.PrimaryArg == 0x01 || payload.TertiaryArg == 0x01;
    }

    private static void AppendReportHex(StringBuilder builder, ReadOnlySpan<byte> report)
    {
        var length = Math.Min(report.Length, ReportLength);
        for (var i = 0; i < length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            AppendByteHex(builder, report[i]);
        }
    }

    private static void AppendByteHex(StringBuilder builder, byte value)
    {
        const string hex = "0123456789abcdef";
        builder.Append(hex[value >> 4]);
        builder.Append(hex[value & 0x0f]);
    }

    private static bool MatchesHeader(ReadOnlySpan<byte> response, byte transactionId, byte commandClass, byte commandId)
    {
        return response.Length >= ReportLength
            && response[1] == transactionId
            && response[6] == commandClass
            && response[7] == commandId;
    }

    private static byte CalculateChecksum(ReadOnlySpan<byte> report)
    {
        byte checksum = 0x00;
        for (var index = 2; index <= 87; index++)
        {
            checksum ^= report[index];
        }

        return checksum;
    }
}

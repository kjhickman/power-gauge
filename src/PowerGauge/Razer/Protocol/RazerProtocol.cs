using System;
using System.Collections.Generic;

namespace PowerGauge.Razer.Protocol;

internal static class RazerProtocol
{
    public const int ReportLength = 90;

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

    public static bool LooksLikeResponse(IReadOnlyList<byte> response, byte transactionId, byte commandClass, byte commandId)
    {
        if (response.Count < ReportLength)
        {
            return false;
        }

        var status = response[0];
        if (status is not (0x00 or 0x02 or 0x04))
        {
            return false;
        }

        return response[1] == transactionId
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

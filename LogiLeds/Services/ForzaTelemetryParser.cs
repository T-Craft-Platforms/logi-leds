using System.Buffers.Binary;
using LogiLeds.Models;

namespace LogiLeds.Services;

public static class ForzaTelemetryParser
{
    public const int DashPacketLength = 324;
    public static readonly int[] SupportedPacketLengths = [232, 311, 323, 324, 331];

    public static bool TryParse(ReadOnlySpan<byte> packet, DateTimeOffset receivedAt, out ForzaTelemetryFrame frame)
    {
        frame = default;
        if (!SupportedPacketLengths.Contains(packet.Length))
        {
            return false;
        }

        var raceValue = BinaryPrimitives.ReadInt32LittleEndian(packet[..4]);
        var timestamp = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4, 4));
        var maxRpm = ReadSingle(packet.Slice(8, 4));
        var idleRpm = ReadSingle(packet.Slice(12, 4));
        var currentRpm = ReadSingle(packet.Slice(16, 4));

        if (raceValue is not (0 or 1) ||
            !float.IsFinite(maxRpm) || !float.IsFinite(idleRpm) || !float.IsFinite(currentRpm) ||
            maxRpm < 0 || idleRpm < 0 || currentRpm < 0 ||
            maxRpm > 100_000 || idleRpm > 100_000 || currentRpm > 150_000)
        {
            return false;
        }

        int? carOrdinal = packet.Length >= 216 ? BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(212, 4)) : null;
        byte? accelerator = null;
        byte? gear = null;
        if (packet.Length is 311 or 331) { accelerator = packet[303]; gear = packet[307]; }
        else if (packet.Length is 323 or 324) { accelerator = packet[315]; gear = packet[319]; }
        frame = new ForzaTelemetryFrame(raceValue == 1, timestamp, maxRpm, idleRpm, currentRpm, receivedAt,
            GetVariant(packet.Length), carOrdinal, gear, accelerator);
        return true;
    }

    private static string GetVariant(int length) => length switch
    {
        232 => "Forza Motorsport Sled",
        311 => "Forza Motorsport 7 Dash",
        331 => "Forza Motorsport Dash",
        323 or 324 => "Forza Horizon Dash",
        _ => "Unknown Forza packet"
    };

    private static float ReadSingle(ReadOnlySpan<byte> bytes) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
}

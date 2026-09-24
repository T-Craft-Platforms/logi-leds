using System.Buffers.Binary;
using LogiLeds.Models;

namespace LogiLeds.Services;

public static class BeamNgOutGaugeParser
{
    // OutGauge is 92 bytes, plus an optional four-byte ID.
    public static bool TryParse(ReadOnlySpan<byte> packet, int maxRpm, DateTimeOffset receivedAt,
        out TelemetryFrame frame)
    {
        frame = default;
        if (packet.Length is not (92 or 96) || !packet.Slice(4, 4).SequenceEqual("beam"u8) ||
            maxRpm is < 1000 or > 30000) return false;

        var rpm = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(16, 4)));
        var throttle = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(48, 4)));
        if (!float.IsFinite(rpm) || rpm < 0 || rpm > 150_000 ||
            !float.IsFinite(throttle) || throttle is < 0 or > 1) return false;

        var gear = packet[10];
        frame = new TelemetryFrame(true, 0, maxRpm, 0, rpm, receivedAt,
            "BeamNG.drive OutGauge", null, gear >= 2 ? (byte)(gear - 1) : null,
            (byte)Math.Round(throttle * 255), TelemetryGame.BeamNg);
        return true;
    }
}

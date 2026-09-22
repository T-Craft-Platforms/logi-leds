using System.Buffers.Binary;
using LogiLeds.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiLeds.Tests;

[TestClass]
public sealed class TelemetryParserTests
{
    [DataTestMethod]
    [DataRow(232)]
    [DataRow(311)]
    [DataRow(323)]
    [DataRow(324)]
    [DataRow(331)]
    public void TryParse_ReadsCommonRpmPrefixForEverySupportedPacket(int length)
    {
        var packet = CreatePacket(true, 123_456, 9_000, 900, 4_500, length);
        var receivedAt = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        Assert.IsTrue(ForzaTelemetryParser.TryParse(packet, receivedAt, out var frame));
        Assert.IsTrue(frame.IsRaceOn);
        Assert.AreEqual((uint)123_456, frame.TimestampMilliseconds);
        Assert.AreEqual(9_000f, frame.EngineMaxRpm);
        Assert.AreEqual(4_500f, frame.CurrentEngineRpm);
        Assert.AreEqual(receivedAt, frame.ReceivedAt);
        Assert.AreNotEqual("Unknown Forza packet", frame.ProtocolVariant);
    }

    [TestMethod]
    public void TryParse_AcceptsRaceOffAndRejectsMalformedPackets()
    {
        Assert.IsTrue(ForzaTelemetryParser.TryParse(CreatePacket(false, 1, 8_000, 800, 0), DateTimeOffset.UtcNow,
            out var frame));
        Assert.IsFalse(frame.IsRaceOn);
        Assert.IsFalse(ForzaTelemetryParser.TryParse(new byte[20], DateTimeOffset.UtcNow, out _));
        var invalidRace = CreatePacket(true, 1, 8_000, 800, 2_000);
        BinaryPrimitives.WriteInt32LittleEndian(invalidRace.AsSpan(0, 4), 2);
        Assert.IsFalse(ForzaTelemetryParser.TryParse(invalidRace, DateTimeOffset.UtcNow, out _));
        Assert.IsFalse(ForzaTelemetryParser.TryParse(CreatePacket(true, 1, float.NaN, 800, 2_000),
            DateTimeOffset.UtcNow, out _));
    }

    internal static byte[] CreatePacket(bool raceOn, uint timestamp, float maxRpm, float idleRpm, float currentRpm,
        int length = 324)
    {
        var packet = new byte[length];
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(0, 4), raceOn ? 1 : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), timestamp);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8, 4), BitConverter.SingleToInt32Bits(maxRpm));
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12, 4), BitConverter.SingleToInt32Bits(idleRpm));
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(16, 4), BitConverter.SingleToInt32Bits(currentRpm));
        return packet;
    }
}
using System.Buffers.Binary;
using LogiWheelForge.Models;
using LogiWheelForge.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiWheelForge.Tests;

[TestClass]
public sealed class BeamNgOutGaugeParserTests
{
    [DataTestMethod]
    [DataRow(92)]
    [DataRow(96)]
    public void TryParse_ReadsBeamNgRpmAndThrottle(int length)
    {
        var packet = CreatePacket(5_200, .75f, length);
        var receivedAt = DateTimeOffset.UtcNow;

        Assert.IsTrue(BeamNgOutGaugeParser.TryParse(packet, 8_000, receivedAt, out var frame));
        Assert.AreEqual(TelemetryGame.BeamNg, frame.Game);
        Assert.AreEqual("BeamNG.drive OutGauge", frame.ProtocolVariant);
        Assert.AreEqual(5_200f, frame.CurrentEngineRpm);
        Assert.AreEqual(8_000f, frame.EngineMaxRpm);
        Assert.AreEqual((byte)3, frame.Gear);
        Assert.AreEqual((byte)191, frame.Accelerator);
        Assert.AreEqual(receivedAt, frame.ReceivedAt);
    }

    [TestMethod]
    public void TryParse_RejectsWrongFormatAndInvalidValues()
    {
        var packet = CreatePacket(4_000, .5f);
        Assert.IsFalse(BeamNgOutGaugeParser.TryParse(packet.AsSpan(0, 91), 7_000,
            DateTimeOffset.UtcNow, out _));
        packet[4] = (byte)'x';
        Assert.IsFalse(BeamNgOutGaugeParser.TryParse(packet, 7_000, DateTimeOffset.UtcNow, out _));
        packet = CreatePacket(float.NaN, .5f);
        Assert.IsFalse(BeamNgOutGaugeParser.TryParse(packet, 7_000, DateTimeOffset.UtcNow, out _));
        packet = CreatePacket(4_000, 1.5f);
        Assert.IsFalse(BeamNgOutGaugeParser.TryParse(packet, 7_000, DateTimeOffset.UtcNow, out _));
    }

    internal static byte[] CreatePacket(float rpm, float throttle, int length = 92)
    {
        var packet = new byte[length];
        "beam"u8.CopyTo(packet.AsSpan(4, 4));
        packet[10] = 4; // Third gear in OutGauge's reverse/neutral/first numbering.
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(16, 4), BitConverter.SingleToInt32Bits(rpm));
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(48, 4), BitConverter.SingleToInt32Bits(throttle));
        return packet;
    }
}

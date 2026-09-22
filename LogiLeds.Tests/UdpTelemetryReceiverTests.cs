using System.Net;
using System.Net.Sockets;
using LogiLeds.Models;
using LogiLeds.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiLeds.Tests;

[TestClass]
public sealed class UdpTelemetryReceiverTests
{
    [TestMethod]
    public async Task Receiver_GetsDashPacketAndStopsCleanly()
    {
        var port = GetUnusedPort();
        await using var receiver = new UdpTelemetryReceiver();
        var received =
            new TaskCompletionSource<ForzaTelemetryFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.FrameReceived += frame => received.TrySetResult(frame);
        await receiver.StartAsync(LedProfileSettings.Defaults with { Port = port });

        using var sender = new UdpClient(AddressFamily.InterNetwork);
        var packet = TelemetryParserTests.CreatePacket(true, 42, 8_500, 850, 4_250);
        await sender.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, port));
        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.AreEqual(4_250f, frame.CurrentEngineRpm);
        await receiver.StopAsync();
        Assert.IsFalse(receiver.IsRunning);
    }

    [TestMethod]
    public async Task Receiver_ReportsOccupiedPortAtStart()
    {
        using var blocker = new UdpClient(AddressFamily.InterNetwork);
        blocker.Client.ExclusiveAddressUse = true;
        blocker.Client.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)blocker.Client.LocalEndPoint!).Port;
        await using var receiver = new UdpTelemetryReceiver();
        await Assert.ThrowsExceptionAsync<SocketException>(() =>
            receiver.StartAsync(LedProfileSettings.Defaults with { Port = port }));
    }

    private static int GetUnusedPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }
}
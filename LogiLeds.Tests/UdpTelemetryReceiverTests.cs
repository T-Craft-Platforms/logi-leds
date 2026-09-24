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
            new TaskCompletionSource<TelemetryFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.FrameReceived += frame => received.TrySetResult(frame);
        await receiver.StartAsync(LedProfileSettings.Defaults with
        {
            TelemetryGames = [TelemetryGameSettings.DefaultForza with { Port = port }]
        });

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
            receiver.StartAsync(LedProfileSettings.Defaults with
            {
                TelemetryGames = [TelemetryGameSettings.DefaultForza with { Port = port }]
            }));
    }

    [TestMethod]
    public async Task Receiver_AutoListensToForzaAndBeamNg()
    {
        var forzaPort = GetUnusedPort();
        var beamPort = GetUnusedPort();
        while (beamPort == forzaPort) beamPort = GetUnusedPort();
        var settings = LedProfileSettings.Defaults with
        {
            TelemetryGames =
            [
                TelemetryGameSettings.DefaultForza with { Port = forzaPort },
                TelemetryGameSettings.DefaultBeamNg with { Port = beamPort, MaxRpm = 8_200 }
            ]
        };
        await using var receiver = new UdpTelemetryReceiver();
        var forza = new TaskCompletionSource<TelemetryFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var beam = new TaskCompletionSource<TelemetryFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.FrameReceived += frame =>
        {
            if (frame.Game == TelemetryGame.Forza) forza.TrySetResult(frame);
            else beam.TrySetResult(frame);
        };
        await receiver.StartAsync(settings);

        using var sender = new UdpClient(AddressFamily.InterNetwork);
        await sender.SendAsync(TelemetryParserTests.CreatePacket(true, 1, 9_000, 900, 4_500),
            new IPEndPoint(IPAddress.Loopback, forzaPort));
        await sender.SendAsync(BeamNgOutGaugeParserTests.CreatePacket(4_200, .7f),
            new IPEndPoint(IPAddress.Loopback, beamPort));

        Assert.AreEqual(4_500f, (await forza.Task.WaitAsync(TimeSpan.FromSeconds(2))).CurrentEngineRpm);
        Assert.AreEqual(8_200f, (await beam.Task.WaitAsync(TimeSpan.FromSeconds(2))).EngineMaxRpm);
    }

    [TestMethod]
    public async Task Receiver_SelectedGameOnlyBindsItsPort()
    {
        var forzaPort = GetUnusedPort();
        var beamPort = GetUnusedPort();
        while (beamPort == forzaPort) beamPort = GetUnusedPort();
        var settings = LedProfileSettings.Defaults with
        {
            TelemetryWatch = TelemetryWatchMode.BeamNg,
            TelemetryGames =
            [
                TelemetryGameSettings.DefaultForza with { Port = forzaPort },
                TelemetryGameSettings.DefaultBeamNg with { Port = beamPort }
            ]
        };
        await using var receiver = new UdpTelemetryReceiver();
        var received = new TaskCompletionSource<TelemetryFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        receiver.FrameReceived += frame => received.TrySetResult(frame);
        await receiver.StartAsync(settings);

        using var otherListener = new UdpClient(AddressFamily.InterNetwork);
        otherListener.Client.ExclusiveAddressUse = true;
        otherListener.Client.Bind(new IPEndPoint(IPAddress.Loopback, forzaPort));
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        await sender.SendAsync(BeamNgOutGaugeParserTests.CreatePacket(3_500, .4f),
            new IPEndPoint(IPAddress.Loopback, beamPort));

        Assert.AreEqual(TelemetryGame.BeamNg,
            (await received.Task.WaitAsync(TimeSpan.FromSeconds(2))).Game);
    }

    private static int GetUnusedPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }
}

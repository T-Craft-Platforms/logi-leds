using System.Text.Json;
using LogiLeds.Models;
using LogiLeds.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiLeds.Tests;

[TestClass]
public sealed class ConfigurationAndLearningTests
{
    [TestMethod]
    public void Catalog_LoadsValidFilesAndIsolatesInvalidOrDuplicateFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "LogiLeds.Tests", Guid.NewGuid().ToString("N"));
        var bundled = Path.Combine(root, "bundled");
        var user = Path.Combine(root, "user");
        Directory.CreateDirectory(bundled);
        Directory.CreateDirectory(user);
        try
        {
            var valid = new WheelDefinition
            {
                Id = "test-wheel", DisplayName = "Test Wheel", ProductIds = [1], PhysicalLedCount = 2,
                ControlGroupCount = 2, Colors = ["#00FF00", "#FF0000"]
            };
            File.WriteAllText(Path.Combine(bundled, "valid.json"), JsonSerializer.Serialize(valid));
            File.WriteAllText(Path.Combine(user, "duplicate.json"), JsonSerializer.Serialize(valid));
            File.WriteAllText(Path.Combine(user, "unsafe.json"),
                JsonSerializer.Serialize(valid with { Id = "unsafe", Transport = "load-a-dll" }));
            var result = new WheelDefinitionCatalog(bundled, user).Load();
            Assert.AreEqual(1, result.Definitions.Count);
            Assert.AreEqual(2, result.Diagnostics.Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task SettingsStore_MigratesLegacyShapeWithoutChangingExplicitEndpoint()
    {
        var path = Path.Combine(Path.GetTempPath(), "LogiLeds.Tests", Guid.NewGuid() + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,
            "{\"BindAddress\":\"127.0.0.1\",\"Port\":5000,\"FirstLedPercent\":65,\"RedlinePercent\":95}");
        var settings = await new SettingsStore(path).LoadAsync();
        Assert.AreEqual("127.0.0.1", settings.BindAddress);
        Assert.AreEqual(5000, settings.Port);
        Assert.AreEqual(5000, settings.TelemetryGames.Single().Port);
        Assert.AreEqual("127.0.0.1", settings.TelemetryGames.Single().BindAddress);
        Assert.AreEqual(65d, settings.FirstLedPercent);
        Assert.IsTrue(settings.AutoStartControl);
        Assert.IsTrue(settings.MinimizeToTray);
    }

    [TestMethod]
    public async Task SettingsStore_MigratesPreviewDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "LogiLeds.Tests", Guid.NewGuid() + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,
            "{\"SchemaVersion\":2,\"FirstLedPercent\":80,\"RedlinePercent\":97.5,\"CloseToTray\":true}");
        var settings = await new SettingsStore(path).LoadAsync();
        Assert.AreEqual(5, settings.SchemaVersion);
        Assert.AreEqual(65d, settings.FirstLedPercent);
        Assert.AreEqual(90d, settings.RedlinePercent);
        Assert.IsTrue(settings.CloseToTray);
        var persisted = await File.ReadAllTextAsync(path);
        StringAssert.Contains(persisted, "\"SchemaVersion\": 5");
        File.Delete(path);
    }

    [TestMethod]
    public async Task SettingsStore_PreservesGameEndpointsAndWatchSelection()
    {
        var path = Path.Combine(Path.GetTempPath(), "LogiLeds.Tests", Guid.NewGuid() + ".json");
        var store = new SettingsStore(path);
        var configured = LedProfileSettings.Defaults with
        {
            TelemetryWatch = TelemetryWatchMode.BeamNg,
            TelemetryGames =
            [
                TelemetryGameSettings.DefaultForza with { BindAddress = "127.0.0.1", Port = 5000 },
                TelemetryGameSettings.DefaultBeamNg with { Port = 6000, MaxRpm = 9200 }
            ]
        };

        await store.SaveAsync(configured);
        var loaded = await store.LoadAsync();

        Assert.AreEqual(TelemetryWatchMode.BeamNg, loaded.TelemetryWatch);
        Assert.AreEqual(2, loaded.TelemetryGames.Length);
        Assert.AreEqual(5000, loaded.TelemetryGames[0].Port);
        Assert.AreEqual(9200, loaded.TelemetryGames[1].MaxRpm);
        File.Delete(path);
    }

    [TestMethod]
    public void SmartLearner_TracksProgressAndLearnsFromUpshiftsAfterThrottleLift()
    {
        var learner =
            new SmartRedlineLearner(Path.Combine(Path.GetTempPath(), "LogiLeds.Tests", Guid.NewGuid() + ".json"));
        double? learned = null;
        var start = DateTimeOffset.UtcNow;
        for (var i = 0; i < 3; i++)
        {
            var at = start.AddSeconds(i);
            learner.Observe(Frame(9_250, (byte)(i + 2), 255, at), "FH6");
            learner.Observe(Frame(9_400, (byte)(i + 2), 255, at.AddMilliseconds(100)), "FH6");
            learned = learner.Observe(Frame(8_100, (byte)(i + 3), 0, at.AddMilliseconds(200)), "FH6");
            var mapping = learner.GetMappings().Single();
            Assert.AreEqual((i + 1) * 20, mapping.ProgressPercent);
            Assert.AreEqual(i + 1, mapping.ShiftCount);
            if (i == 0) Assert.IsNull(mapping.LearnedRedlinePercent);
        }

        Assert.IsNotNull(learned);
        Assert.AreEqual(94d, learned.Value, .1);
        Assert.AreEqual(60, learner.GetMappings().Single().ConfidencePercent);
    }

    [TestMethod]
    public async Task SmartLearner_PersistsPartialProgressAndReadsLegacyCalibration()
    {
        var path = Path.Combine(Path.GetTempPath(), "LogiLeds.Tests", Guid.NewGuid() + ".json");
        var learner = new SmartRedlineLearner(path);
        var at = DateTimeOffset.UtcNow;
        learner.Observe(Frame(9_300, 2, 255, at), "FH6");
        learner.Observe(Frame(9_400, 2, 255, at.AddMilliseconds(100)), "FH6");
        learner.Observe(Frame(8_100, 3, 0, at.AddMilliseconds(200)), "FH6");
        await learner.FlushAsync();
        var loaded = new SmartRedlineLearner(path);
        await loaded.LoadAsync();
        Assert.AreEqual(20, loaded.GetMappings().Single().ProgressPercent);

        await File.WriteAllTextAsync(path, "{\"FH6|Forza Horizon Dash|42|10000\":94.5}");
        var legacy = new SmartRedlineLearner(path);
        await legacy.LoadAsync();
        Assert.AreEqual(94.5, legacy.GetMappings().Single().LearnedRedlinePercent);
        Assert.AreEqual(100, legacy.GetMappings().Single().ProgressPercent);
    }

    [TestMethod]
    public void SmartLearner_IgnoresDownshiftsAndIsolatedThrottleFrames()
    {
        var learner =
            new SmartRedlineLearner(Path.Combine(Path.GetTempPath(), "LogiLeds.Tests", Guid.NewGuid() + ".json"));
        var at = DateTimeOffset.UtcNow;
        learner.Observe(Frame(9_400, 3, 255, at), "FH6");
        learner.Observe(Frame(8_000, 4, 0, at.AddMilliseconds(100)), "FH6");
        learner.Observe(Frame(9_400, 4, 255, at.AddSeconds(1)), "FH6");
        learner.Observe(Frame(9_400, 4, 255, at.AddSeconds(1.1)), "FH6");
        learner.Observe(Frame(8_000, 3, 0, at.AddSeconds(1.2)), "FH6");
        Assert.AreEqual(0, learner.GetMappings().Count);
    }

    [TestMethod]
    public void SmartLearner_UsesMatchingClusterAndLowersConfidenceForOutlier()
    {
        var learner =
            new SmartRedlineLearner(Path.Combine(Path.GetTempPath(), "LogiLeds.Tests", Guid.NewGuid() + ".json"));
        var start = DateTimeOffset.UtcNow;
        var peaks = new[] { 9_400f, 9_350f, 9_450f, 8_000f };
        for (var i = 0; i < peaks.Length; i++)
        {
            var at = start.AddSeconds(i);
            learner.Observe(Frame(peaks[i] - 100, (byte)(i + 2), 255, at), "FH6");
            learner.Observe(Frame(peaks[i], (byte)(i + 2), 255, at.AddMilliseconds(100)), "FH6");
            learner.Observe(Frame(7_500, (byte)(i + 3), 0, at.AddMilliseconds(200)), "FH6");
        }

        var mapping = learner.GetMappings().Single();
        Assert.AreEqual(80, mapping.ProgressPercent);
        Assert.AreEqual(94d, mapping.LearnedRedlinePercent!.Value, .5);
        Assert.IsTrue(mapping.ConfidencePercent < 60);
    }

    private static TelemetryFrame Frame(float rpm, byte gear, byte throttle, DateTimeOffset at)
    {
        return new TelemetryFrame(true, 1, 10_000, 900, rpm, at,
            "Forza Horizon Dash", 42, gear, throttle);
    }
}

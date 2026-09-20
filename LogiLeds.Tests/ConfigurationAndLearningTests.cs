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
        var bundled = Path.Combine(root, "bundled"); var user = Path.Combine(root, "user");
        Directory.CreateDirectory(bundled); Directory.CreateDirectory(user);
        try
        {
            var valid = new WheelDefinition { Id = "test-wheel", DisplayName = "Test Wheel", ProductIds = [1], PhysicalLedCount = 2, ControlGroupCount = 2, Colors = ["#00FF00", "#FF0000"] };
            File.WriteAllText(Path.Combine(bundled, "valid.json"), JsonSerializer.Serialize(valid));
            File.WriteAllText(Path.Combine(user, "duplicate.json"), JsonSerializer.Serialize(valid));
            File.WriteAllText(Path.Combine(user, "unsafe.json"), JsonSerializer.Serialize(valid with { Id = "unsafe", Transport = "load-a-dll" }));
            var result = new WheelDefinitionCatalog(bundled, user).Load();
            Assert.AreEqual(1, result.Definitions.Count);
            Assert.AreEqual(2, result.Diagnostics.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task SettingsStore_MigratesLegacyShapeWithoutChangingExplicitEndpoint()
    {
        var path = Path.Combine(Path.GetTempPath(), "LogiLeds.Tests", Guid.NewGuid() + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{\"BindAddress\":\"127.0.0.1\",\"Port\":5000,\"FirstLedPercent\":65,\"RedlinePercent\":95}");
        var settings = await new SettingsStore(path).LoadAsync();
        Assert.AreEqual("127.0.0.1", settings.BindAddress);
        Assert.AreEqual(5000, settings.Port);
        Assert.AreEqual(65d, settings.FirstLedPercent);
        Assert.IsTrue(settings.AutoStartControl);
        Assert.IsTrue(settings.MinimizeToTray);
    }

    [TestMethod]
    public async Task SettingsStore_MigratesPreviewDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "LogiLeds.Tests", Guid.NewGuid() + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{\"SchemaVersion\":2,\"FirstLedPercent\":80,\"RedlinePercent\":97.5,\"CloseToTray\":true}");
        var settings = await new SettingsStore(path).LoadAsync();
        Assert.AreEqual(4, settings.SchemaVersion);
        Assert.AreEqual(65d, settings.FirstLedPercent);
        Assert.AreEqual(90d, settings.RedlinePercent);
        Assert.IsTrue(settings.CloseToTray);
        var persisted = await File.ReadAllTextAsync(path);
        StringAssert.Contains(persisted, "\"SchemaVersion\": 4");
        File.Delete(path);
    }

    [TestMethod]
    public void SmartLearner_RequiresThreeConsistentEvents()
    {
        var learner = new SmartRedlineLearner(Path.Combine(Path.GetTempPath(), "LogiLeds.Tests", Guid.NewGuid() + ".json"));
        double? learned = null;
        for (var i = 0; i < 3; i++)
        {
            learned = learner.Observe(Frame(9_400), "FH6");
            learned = learner.Observe(Frame(8_500), "FH6");
        }
        Assert.IsNotNull(learned);
        Assert.AreEqual(94d, learned.Value, .1);
    }

    private static ForzaTelemetryFrame Frame(float rpm) => new(true, 1, 10_000, 900, rpm, DateTimeOffset.UtcNow,
        "Forza Horizon Dash", CarOrdinal: 42, Gear: 3, Accelerator: 255);
}

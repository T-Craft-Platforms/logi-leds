using LogiLeds.Models;
using LogiLeds.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiLeds.Tests;

[TestClass]
public sealed class SettingsAndLedMathTests
{
    [TestMethod]
    public void Defaults_ArePremiumSafeDefaults()
    {
        var settings = LedProfileSettings.Defaults;
        Assert.IsTrue(settings.TryValidate(out _));
        Assert.AreEqual("0.0.0.0", settings.BindAddress);
        Assert.AreEqual(1024, settings.Port);
        Assert.AreEqual(65d, settings.FirstLedPercent);
        Assert.AreEqual(90d, settings.RedlinePercent);
        Assert.IsTrue(settings.AutoStartControl);
        Assert.IsTrue(settings.MinimizeToTray);
        Assert.IsFalse(settings.CloseToTray);
        Assert.IsTrue(settings.BlinkAtRedline);
    }

    [TestMethod]
    public void Validation_RejectsBadEndpointAndThresholdOrder()
    {
        Assert.IsFalse((LedProfileSettings.Defaults with { BindAddress = "not-an-ip" }).TryValidate(out _));
        Assert.IsFalse((LedProfileSettings.Defaults with { Port = 0 }).TryValidate(out _));
        Assert.IsFalse((LedProfileSettings.Defaults with { FirstLedPercent = 96, RedlinePercent = 95 }).TryValidate(out _));
        Assert.IsFalse((LedProfileSettings.Defaults with { AdvancedThresholds = [90, 85] }).TryValidate(out _));
    }

    [TestMethod]
    public void LedMath_MapsFiveAndTenStageProfiles()
    {
        Assert.AreEqual((0, false), LedMath.CalculatePreview(7_900, 10_000, 80, 95));
        Assert.AreEqual((1, false), LedMath.CalculatePreview(8_000, 10_000, 80, 95));
        Assert.AreEqual((5, true), LedMath.CalculatePreview(9_500, 10_000, 80, 95));
        var ten = LedMath.BuildRecommendedThresholds(10);
        Assert.AreEqual(10, ten.Length);
        Assert.AreEqual(65d, ten[0]);
        Assert.AreEqual(89d, ten[^1]);
        Assert.AreEqual((9, false), LedMath.CalculatePreview(8_800, 10_000, 80, 95, 10, ten));
    }
}

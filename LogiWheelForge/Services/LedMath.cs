using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

public static class LedMath
{
    public static (float FirstLedRpm, float RedlineRpm) CalculateThresholds(float maximumRpm, double firstPercent,
        double redlinePercent)
    {
        return (maximumRpm * (float)(firstPercent / 100d), maximumRpm * (float)(redlinePercent / 100d));
    }

    public static double[] BuildRecommendedThresholds(int groupCount,
        double firstPercent = LedProfileSettings.DefaultFirstLedPercent,
        double lastPercent = LedProfileSettings.DefaultRedlinePercent - 5)
    {
        if (groupCount <= 0) return [];
        if (groupCount == 1) return [firstPercent];
        return Enumerable.Range(0, groupCount)
            .Select(i =>
            {
                var position = i / (groupCount - 1d);
                // Keep early LEDs calm and pack the last groups toward the shift point.
                var progression = Math.Pow(position, 1.4);
                return firstPercent + (lastPercent - firstPercent) * progression;
            })
            .ToArray();
    }

    public static (int Count, bool IsFlashing) CalculatePreview(
        float currentRpm, float maximumRpm, double firstPercent, double redlinePercent,
        int groupCount = 5, IReadOnlyList<double>? advancedThresholds = null)
    {
        if (maximumRpm <= 0 || currentRpm <= 0 || groupCount <= 0) return (0, false);
        var percent = currentRpm / (double)maximumRpm * 100d;
        var flashing = percent >= redlinePercent;
        var thresholds = advancedThresholds is { Count: > 0 }
            ? advancedThresholds
            : BuildRecommendedThresholds(groupCount, firstPercent, Math.Max(firstPercent, redlinePercent - 5));
        var count = thresholds.Take(groupCount).Count(x => percent >= x);
        return (Math.Clamp(count, 0, groupCount), flashing);
    }
}
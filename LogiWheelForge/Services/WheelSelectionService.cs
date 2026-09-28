using System.Windows.Threading;
using HidSharp;
using HidSharp.Reports;
using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

/// <summary>Selects one Logitech wheel for every module. A separate USB pedal set is not a wheel candidate.</summary>
public sealed class WheelSelectionService : IDisposable
{
    private readonly IReadOnlyList<WheelDefinition> _definitions;
    private readonly DispatcherTimer _timer;
    private HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);
    private string? _preferred;
    private string? _lastConnected;

    public WheelSelectionService(IReadOnlyList<WheelDefinition> definitions)
    {
        _definitions = definitions;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
    }

    public string? ActiveWheelId { get; private set; }
    public event EventHandler<string?>? ActiveWheelChanged;
    public void Start() { Refresh(); _timer.Start(); }
    public void Dispose() => _timer.Stop();
    public void SetPreferredWheel(string? wheelId) { _preferred = wheelId; Refresh(); }

    public void Refresh()
    {
        var candidates = new List<(string Path, string WheelId)>();
        try
        {
            foreach (var device in DeviceList.Local.GetHidDevices(0x046d))
            {
                try
                {
                    var descriptor = device.GetReportDescriptor();
                    var definition = _definitions.FirstOrDefault(item => item.ProductIds.Contains(device.ProductID));
                    string productName;
                    try { productName = device.GetProductName(); }
                    catch { productName = string.Empty; }
                    if (definition?.IsPedalSet == true ||
                        productName.Contains("pedal", StringComparison.OrdinalIgnoreCase)) continue;
                    if (definition is null && !productName.Contains("wheel", StringComparison.OrdinalIgnoreCase) &&
                        !productName.Contains("racing", StringComparison.OrdinalIgnoreCase)) continue;
                    var isWheel = descriptor.DeviceItems.Any(item =>
                        item.Usages.GetAllValues().Any(usage => (uint)usage is 0x00010004 or 0x00010005 or 0x00010008) &&
                        item.InputReports.SelectMany(report => report.DataItems)
                            .Any(data => data.Usages.GetAllValues().Any(usage => (uint)usage == 0x00010030)));
                    if (!isWheel) continue;
                    var id = definition?.Id ?? $"logitech-{device.ProductID:x4}";
                    candidates.Add((device.DevicePath, id));
                }
                catch { /* Ignore non-controller interfaces and transient enumeration errors. */ }
            }
        }
        catch { return; }
        var paths = candidates.Select(candidate => candidate.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var arrivals = candidates.Where(candidate => !_knownPaths.Contains(candidate.Path)).ToArray();
        if (arrivals.Length > 0) _lastConnected = arrivals[^1].WheelId;
        _knownPaths = paths;
        if (!candidates.Any(candidate => candidate.WheelId == _lastConnected))
            _lastConnected = candidates.LastOrDefault().WheelId;
        var selected = _preferred is null ? _lastConnected :
            candidates.Any(candidate => candidate.WheelId == _preferred) ? _preferred : null;
        if (selected == ActiveWheelId) return;
        ActiveWheelId = selected;
        ActiveWheelChanged?.Invoke(this, selected);
    }
}

using LogiLeds.Models;

namespace LogiLeds.Services;

public interface IWheelLedController : IDisposable
{
    bool IsConnected { get; }
    string WheelName { get; }
    string StatusMessage { get; }
    WheelDefinition? CurrentDefinition { get; }
    IReadOnlyList<WheelDefinition> AvailableDefinitions { get; }
    IReadOnlyList<string> DefinitionDiagnostics { get; }

    bool Initialize(nint windowHandle);
    void SetPreferredWheel(string? wheelId);
    void Refresh();
    void RefreshNow();
    bool SetLevel(int illuminatedGroups);
    void ClearLeds();
    Task TestLedsAsync(CancellationToken cancellationToken = default);
    Task PlayReadyAnimationAsync(CancellationToken cancellationToken = default);
    void Shutdown();
}

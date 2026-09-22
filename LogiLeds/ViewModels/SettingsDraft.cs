using System.Globalization;
using LogiLeds.Models;
using LogiLeds.Services;

namespace LogiLeds.ViewModels;

/// <summary>Shared editable settings so Save on either configuration page applies the complete draft.</summary>
public sealed class SettingsDraft : ObservableObject
{
    private string _bindAddress = LedProfileSettings.DefaultBindAddress;
    private bool _blinkAtRedline = true, _learnPerCarShift = true, _autoStartControl = true, _closeToTray = true;
    private double _firstLedPercent = LedProfileSettings.DefaultFirstLedPercent;
    private string _port = LedProfileSettings.DefaultPort.ToString(CultureInfo.InvariantCulture);
    private RpmProfileMode _profileMode;
    private double _redlinePercent = LedProfileSettings.DefaultRedlinePercent;
    private WheelOption? _selectedWheel;
    private AppTheme _theme = AppTheme.System;

    public string BindAddress
    {
        get => _bindAddress;
        set => SetField(ref _bindAddress, value);
    }

    public string Port
    {
        get => _port;
        set => SetField(ref _port, value);
    }

    public double FirstLedPercent
    {
        get => _firstLedPercent;
        set => SetField(ref _firstLedPercent, Math.Round(value, 1));
    }

    public double RedlinePercent
    {
        get => _redlinePercent;
        set => SetField(ref _redlinePercent, Math.Round(value, 1));
    }

    public bool BlinkAtRedline
    {
        get => _blinkAtRedline;
        set => SetField(ref _blinkAtRedline, value);
    }

    public bool LearnPerCarShift
    {
        get => _learnPerCarShift;
        set => SetField(ref _learnPerCarShift, value);
    }

    public bool AutoStartControl
    {
        get => _autoStartControl;
        set => SetField(ref _autoStartControl, value);
    }

    public bool CloseToTray
    {
        get => _closeToTray;
        set => SetField(ref _closeToTray, value);
    }

    public AppTheme Theme
    {
        get => _theme;
        set
        {
            if (SetField(ref _theme, value)) ThemeService.Apply(value);
        }
    }

    public RpmProfileMode ProfileMode
    {
        get => _profileMode;
        set
        {
            if (!SetField(ref _profileMode, value)) return;
            OnPropertyChanged(nameof(IsEasyMode));
            OnPropertyChanged(nameof(IsAdvancedMode));
        }
    }

    public bool IsEasyMode
    {
        get => ProfileMode == RpmProfileMode.Easy;
        set
        {
            if (value) ProfileMode = RpmProfileMode.Easy;
        }
    }

    public bool IsAdvancedMode
    {
        get => ProfileMode == RpmProfileMode.Advanced;
        set
        {
            if (value) ProfileMode = RpmProfileMode.Advanced;
        }
    }

    public WheelOption? SelectedWheel
    {
        get => _selectedWheel;
        set => SetField(ref _selectedWheel, value);
    }
}
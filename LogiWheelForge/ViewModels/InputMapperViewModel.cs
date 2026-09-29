using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using LogiWheelForge.Commands;
using LogiWheelForge.Models;
using LogiWheelForge.Services;
using LogiWheelForge.Views;

namespace LogiWheelForge.ViewModels;

public sealed class MapperRuleDraft : ObservableObject
{
    private MapperRule _value;
    public MapperRuleDraft(MapperRule rule) => _value = rule;
    public string Id => _value.Id;
    public string Control { get => _value.Control; set => Change(_value with { Control = value }); }
    public MapperTriggerKind Trigger { get => _value.Trigger; set => Change(_value with { Trigger = value }); }
    public MapperStepMode StepMode { get => _value.StepMode; set => Change(_value with { StepMode = value }); }
    public double StepPercent { get => _value.StepPercent; set => Change(_value with { StepPercent = value }); }
    public double MinPercent { get => _value.MinPercent; set => Change(_value with { MinPercent = value }); }
    public double MaxPercent { get => _value.MaxPercent; set => Change(_value with { MaxPercent = value }); }
    public double HysteresisPercent { get => _value.HysteresisPercent; set => Change(_value with { HysteresisPercent = value }); }
    public double DeadZonePercent { get => _value.DeadZonePercent; set => Change(_value with { DeadZonePercent = value }); }
    public double CurveExponent { get => _value.CurveExponent; set => Change(_value with { CurveExponent = value }); }
    public int Direction { get => _value.Direction; set => Change(_value with { Direction = value }); }
    public MapperActionKind Action { get => _value.Action; set => Change(_value with { Action = value }); }
    public MapperActionMode Mode { get => _value.Mode; set => Change(_value with { Mode = value }); }
    public string Output { get => _value.Output; set => Change(_value with { Output = value }); }
    public int DurationMs { get => _value.DurationMs; set => Change(_value with { DurationMs = value }); }
    public double OutputScale { get => _value.OutputScale; set => Change(_value with { OutputScale = value }); }
    public double ForceStrength { get => _value.ForceStrength; set => Change(_value with { ForceStrength = value }); }
    public double TargetPercent { get => _value.TargetPercent; set => Change(_value with { TargetPercent = value }); }
    public MapperForceRelease ForceRelease { get => _value.ForceRelease; set => Change(_value with { ForceRelease = value }); }
    public bool IsStepTrigger => Trigger == MapperTriggerKind.AxisStep;
    public bool IsRangeTrigger => Trigger == MapperTriggerKind.AxisRange;
    public bool IsAxisTrigger => Trigger != MapperTriggerKind.Button;
    public bool IsKeyOrButtonAction => Action is MapperActionKind.Key or MapperActionKind.MouseButton;
    public bool IsMovementAction => Action is MapperActionKind.MouseMove or MapperActionKind.MouseScroll;
    public bool IsHoldTargetAction => Action == MapperActionKind.HoldTarget;
    public bool IsForceAction => Action is MapperActionKind.HoldTarget or MapperActionKind.ReleaseTarget;
    public bool IsTapAction => IsKeyOrButtonAction && Mode == MapperActionMode.Tap;
    public bool UsesOutput => IsKeyOrButtonAction || Action == MapperActionKind.MouseMove;
    public string Summary => $"{Control} · {Trigger} → {Action} {Output}";
    public MapperRule Build() => _value;
    private void Change(MapperRule value)
    {
        _value = value;
        OnPropertyChanged(string.Empty);
        OnPropertyChanged(nameof(Summary));
    }
}

public sealed class MapperProfileDraft : ObservableObject
{
    private string _name;
    private bool _enabled;
    private bool _resistanceEnabled, _detentsEnabled;
    private double _centerStrength, _dampingStrength, _detentSpacingPercent, _detentHysteresisPercent, _detentStrength;

    public MapperProfileDraft(InputMapperProfile profile)
    {
        Id = profile.Id; _name = profile.Name; _enabled = profile.Enabled;
        ProcessPaths = new ObservableCollection<string>(profile.ProcessPaths);
        Rules = new ObservableCollection<MapperRuleDraft>(profile.Rules.Select(rule => new MapperRuleDraft(rule)));
        _resistanceEnabled = profile.Resistance.Enabled;
        _centerStrength = profile.Resistance.CenterStrength;
        _dampingStrength = profile.Resistance.DampingStrength;
        _detentsEnabled = profile.Resistance.DetentsEnabled;
        _detentSpacingPercent = profile.Resistance.DetentSpacingPercent;
        _detentHysteresisPercent = profile.Resistance.DetentHysteresisPercent;
        _detentStrength = profile.Resistance.DetentStrength;
        PropertyChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        ProcessPaths.CollectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        foreach (var rule in Rules) rule.PropertyChanged += OnRuleChanged;
        Rules.CollectionChanged += OnRulesChanged;
    }
    public event EventHandler? Changed;
    public string Id { get; }
    public string Name { get => _name; set => SetField(ref _name, value); }
    public bool Enabled { get => _enabled; set => SetField(ref _enabled, value); }
    public ObservableCollection<string> ProcessPaths { get; }
    public ObservableCollection<MapperRuleDraft> Rules { get; }
    public bool ResistanceEnabled { get => _resistanceEnabled; set => SetField(ref _resistanceEnabled, value); }
    public double CenterStrength { get => _centerStrength; set => SetField(ref _centerStrength, value); }
    public double DampingStrength { get => _dampingStrength; set => SetField(ref _dampingStrength, value); }
    public bool DetentsEnabled { get => _detentsEnabled; set => SetField(ref _detentsEnabled, value); }
    public double DetentSpacingPercent { get => _detentSpacingPercent; set => SetField(ref _detentSpacingPercent, value); }
    public double DetentHysteresisPercent { get => _detentHysteresisPercent; set => SetField(ref _detentHysteresisPercent, value); }
    public double DetentStrength { get => _detentStrength; set => SetField(ref _detentStrength, value); }
    public InputMapperProfile Build() => new()
    {
        Id = Id, Name = Name?.Trim() ?? string.Empty, Enabled = Enabled,
        ProcessPaths = ProcessPaths.Select(path => Path.GetFullPath(path.Trim())).ToArray(),
        Rules = Rules.Select(rule => rule.Build()).ToArray(),
        Resistance = new MapperResistance
        {
            Enabled = ResistanceEnabled, CenterStrength = CenterStrength, DampingStrength = DampingStrength,
            DetentsEnabled = DetentsEnabled, DetentSpacingPercent = DetentSpacingPercent,
            DetentHysteresisPercent = DetentHysteresisPercent,
            DetentStrength = DetentStrength
        }
    };

    private void OnRuleChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
    private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (MapperRuleDraft rule in e.OldItems) rule.PropertyChanged -= OnRuleChanged;
        if (e.NewItems is not null)
            foreach (MapperRuleDraft rule in e.NewItems) rule.PropertyChanged += OnRuleChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class InputMapperViewModel : ObservableObject, IDisposable
{
    private readonly InputMapperService _service;
    private MapperProfileDraft? _selectedProfile;
    private MapperRuleDraft? _selectedRule;
    private string _status = "Input Mapper is stopped", _inputPreview = "Move a wheel control to preview it";
    private string _manualProcessPath = string.Empty;
    private string? _selectedProcessPath;
    private bool _isRunning;
    private bool _hasUnsavedChanges;
    private string _pageTitle = "Input Mapper";

    public InputMapperViewModel(InputMapperService service)
    {
        _service = service;
        _service.SnapshotChanged += OnSnapshot;
        _service.InputPreview += OnInputPreview;
        AddProfileCommand = new RelayCommand(AddProfile);
        DeleteProfileCommand = new RelayCommand(DeleteProfile);
        AddRuleCommand = new RelayCommand(AddRule);
        DeleteRuleCommand = new RelayCommand(DeleteRule);
        AddProcessCommand = new RelayCommand(AddManualProcess);
        RemoveProcessCommand = new RelayCommand(RemoveProcess);
        PickWindowCommand = new RelayCommand(PickWindow);
        SaveCommand = new AsyncRelayCommand(SaveChangesAsync, onError: ex => Status = $"Could not save: {ex.Message}");
        ToggleCommand = new RelayCommand(() =>
        {
            try { if (_service.IsRunning) _service.Stop(); else _service.Start(); }
            catch (Exception ex) { Status = $"Could not start mapper: {ex.Message}"; }
        });
    }

    public ObservableCollection<MapperProfileDraft> Profiles { get; } = [];
    public string PageTitle { get => _pageTitle; set => SetField(ref _pageTitle, value); }
    public ObservableCollection<string> Controls { get; } = [];
    public IReadOnlyList<MapperTriggerKind> Triggers { get; } = Enum.GetValues<MapperTriggerKind>();
    public IReadOnlyList<MapperStepMode> StepModes { get; } = Enum.GetValues<MapperStepMode>();
    public IReadOnlyList<MapperActionKind> Actions { get; } = Enum.GetValues<MapperActionKind>();
    public IReadOnlyList<MapperActionMode> Modes { get; } = Enum.GetValues<MapperActionMode>();
    public IReadOnlyList<MapperForceRelease> ForceReleases { get; } = Enum.GetValues<MapperForceRelease>();
    public IReadOnlyList<int> Directions { get; } = [-1, 1];
    public ICommand AddProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand AddRuleCommand { get; }
    public ICommand DeleteRuleCommand { get; }
    public ICommand AddProcessCommand { get; }
    public ICommand RemoveProcessCommand { get; }
    public ICommand PickWindowCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ToggleCommand { get; }
    public MapperProfileDraft? SelectedProfile
    {
        get => _selectedProfile;
        set { if (SetField(ref _selectedProfile, value)) SelectedRule = value?.Rules.FirstOrDefault(); }
    }
    public MapperRuleDraft? SelectedRule { get => _selectedRule; set => SetField(ref _selectedRule, value); }
    public string? SelectedProcessPath { get => _selectedProcessPath; set => SetField(ref _selectedProcessPath, value); }
    public string ManualProcessPath { get => _manualProcessPath; set => SetField(ref _manualProcessPath, value); }
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public string InputPreview { get => _inputPreview; private set => SetField(ref _inputPreview, value); }
    public string ToggleText => _isRunning ? "Stop mapper" : "Start mapper";
    public bool IsRunning => _isRunning;
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetField(ref _hasUnsavedChanges, value);
    }
    public string EditStatus => HasUnsavedChanges ? "Unsaved changes" : "All changes saved";

    public void Initialize()
    {
        Profiles.Clear();
        foreach (var profile in _service.Profiles)
        {
            var draft = new MapperProfileDraft(profile);
            draft.Changed += OnDraftChanged;
            Profiles.Add(draft);
        }
        SelectedProfile = Profiles.FirstOrDefault();
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(EditStatus));
    }

    public void Dispose()
    {
        _service.SnapshotChanged -= OnSnapshot;
        _service.InputPreview -= OnInputPreview;
    }

    private void AddProfile()
    {
        var profile = new InputMapperProfile
        {
            Name = "New mapping",
            Rules =
            [
                new MapperRule { Control = "Steering", Direction = 1, Output = "D" },
                new MapperRule { Control = "Steering", Direction = -1, Output = "A" },
                new MapperRule { Control = "Accelerator", Trigger = MapperTriggerKind.AxisRange,
                    Mode = MapperActionMode.Hold, Output = "W", MinPercent = 10 },
                new MapperRule { Control = "Brake", Trigger = MapperTriggerKind.AxisRange,
                    Mode = MapperActionMode.Hold, Output = "S", MinPercent = 10 }
            ]
        };
        var draft = new MapperProfileDraft(profile);
        draft.Changed += OnDraftChanged;
        Profiles.Add(draft); SelectedProfile = draft;
        MarkUnsaved();
        Status = "Add a target executable, review the starter rules, then save";
    }
    private void DeleteProfile()
    {
        if (SelectedProfile is null) return;
        SelectedProfile.Changed -= OnDraftChanged;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
        MarkUnsaved();
    }
    private void AddRule()
    {
        if (SelectedProfile is null) return;
        var rule = new MapperRuleDraft(new MapperRule());
        SelectedProfile.Rules.Add(rule); SelectedRule = rule;
    }
    private void DeleteRule()
    {
        if (SelectedProfile is null || SelectedRule is null) return;
        SelectedProfile.Rules.Remove(SelectedRule);
        SelectedRule = SelectedProfile.Rules.FirstOrDefault();
    }
    private void AddManualProcess()
    {
        if (SelectedProfile is null) return;
        var path = ManualProcessPath.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        { Status = "Enter a full .exe path"; return; }
        AddProcess(path);
        ManualProcessPath = string.Empty;
    }
    private void AddProcess(string path)
    {
        if (SelectedProfile is null) return;
        if (SelectedProfile.ProcessPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        { Status = "This executable is already in the profile"; return; }
        SelectedProfile.ProcessPaths.Add(path);
    }
    private void RemoveProcess()
    {
        if (SelectedProfile is null || SelectedProcessPath is null) return;
        SelectedProfile.ProcessPaths.Remove(SelectedProcessPath);
        SelectedProcessPath = null;
    }
    private void PickWindow()
    {
        var path = ProcessWindowPicker.Pick();
        if (path is null) { Status = "No executable was selected"; return; }
        AddProcess(path);
    }
    public async Task SaveChangesAsync()
    {
        var profiles = Profiles.Select(profile => profile.Build()).ToArray();
        await _service.SaveProfilesAsync(profiles);
        Status = "Profiles saved";
        HasUnsavedChanges = false;
        OnPropertyChanged(nameof(EditStatus));
    }
    private void OnDraftChanged(object? sender, EventArgs e) => MarkUnsaved();
    private void MarkUnsaved()
    {
        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(EditStatus));
    }
    private void OnSnapshot(object? sender, InputMapperSnapshot snapshot)
    {
        _isRunning = snapshot.IsRunning;
        Status = snapshot.ActiveProfile is null || snapshot.Status != "Mapping active" ? snapshot.Status :
            $"{snapshot.ActiveProfile} · {snapshot.ActiveWheel ?? "No wheel"} · {snapshot.ForceStatus}";
        OnPropertyChanged(nameof(ToggleText));
        OnPropertyChanged(nameof(IsRunning));
    }
    private void OnInputPreview(object? sender, WheelInputSample sample)
    {
        InputPreview = $"{sample.Control}: {sample.Percent:0.0}%";
        foreach (var name in _service.AvailableControls)
            if (!Controls.Contains(name)) Controls.Add(name);
    }
}

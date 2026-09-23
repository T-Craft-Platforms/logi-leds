using System.Windows;
using LogiLeds.Models;
using Microsoft.Win32;
using Application = System.Windows.Application;
using Cursors = System.Windows.Input.Cursors;

namespace LogiLeds.Services;

public static class ThemeService
{
    private static AppTheme _selected = AppTheme.System;
    private static bool _usePointerCursors = false;
    private static bool _initialized;

    public static bool UsePointerCursors
    {
        get => _usePointerCursors;
        set
        {
            _usePointerCursors = value;
            if (Application.Current is not { } app) return;
            app.Resources["InteractiveCursor"] = value
                ? Cursors.Hand
                : Cursors.Arrow;
        }
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            if (_selected == AppTheme.System) Apply(_selected);
        };
    }

    public static void Apply(AppTheme theme)
    {
        _selected = theme;
        var effective = theme == AppTheme.System ? GetSystemTheme() : theme;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(x =>
            x.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true);
        if (existing is not null) dictionaries.Remove(existing);
        dictionaries.Insert(0,
            new ResourceDictionary { Source = new Uri($"Themes/{effective}.xaml", UriKind.Relative) });
    }

    private static AppTheme GetSystemTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme",
                0);
            return value is int i && i != 0 ? AppTheme.Light : AppTheme.Dark;
        }
        catch
        {
            return AppTheme.Dark;
        }
    }
}
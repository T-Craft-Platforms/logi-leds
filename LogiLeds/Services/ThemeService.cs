using Microsoft.Win32;
using System.Windows;

namespace LogiLeds.Services;

public static class ThemeService
{
    private static Models.AppTheme _selected = Models.AppTheme.System;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        SystemEvents.UserPreferenceChanged += (_, _) => { if (_selected == Models.AppTheme.System) Apply(_selected); };
    }

    public static void Apply(Models.AppTheme theme)
    {
        _selected = theme;
        var effective = theme == Models.AppTheme.System ? GetSystemTheme() : theme;
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(x => x.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true);
        if (existing is not null) dictionaries.Remove(existing);
        dictionaries.Insert(0, new ResourceDictionary { Source = new Uri($"Themes/{effective}.xaml", UriKind.Relative) });
    }

    private static Models.AppTheme GetSystemTheme()
    {
        try
        {
            var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0);
            return value is int i && i != 0 ? Models.AppTheme.Light : Models.AppTheme.Dark;
        }
        catch { return Models.AppTheme.Dark; }
    }
}

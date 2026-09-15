using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using CalendarFlyout.Native;

namespace CalendarFlyout.Services;

internal sealed class ThemeService : IDisposable
{
    private readonly Window _window;
    private bool _disposed;
    public ThemeService(Window window)
    {
        _window = window;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
    }

    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
    {
        if (!_window.Dispatcher.HasShutdownStarted)
            _window.Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) Apply(); }));
    }

    public void Apply()
    {
        var dark = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (System.Security.SecurityException) { }
        var highContrast = SystemParameters.HighContrast;
        var acrylic = WindowEffects.ApplyTheme(_window, dark, highContrast);
        var resources = Application.Current.Resources;
        void Set(string key, Color color) => resources[key] = new SolidColorBrush(color);
        Color Hex(string text) => (Color)ColorConverter.ConvertFromString(text);
        Set("PanelBrush", highContrast ? SystemColors.WindowColor : Hex(dark ? (acrylic ? "#D0202020" : "#202020") : (acrylic ? "#D0F3F3F3" : "#F3F3F3")));
        Set("TextBrush", highContrast ? SystemColors.WindowTextColor : Hex(dark ? "#F5F5F5" : "#1B1B1B"));
        Set("MutedBrush", highContrast ? SystemColors.WindowTextColor : Hex(dark ? "#BDBDBD" : "#555555"));
        Set("CardBrush", highContrast ? SystemColors.WindowColor : Hex(dark ? "#B3323232" : "#CFFFFFFF"));
        Set("LineBrush", highContrast ? SystemColors.WindowTextColor : Hex(dark ? "#33FFFFFF" : "#22000000"));
        Set("AccentBrush", highContrast ? SystemColors.HighlightColor : Hex(dark ? "#75BFFF" : "#0067C0"));
    }

    public void Dispose()
    {
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= PreferenceChanged;
    }
}

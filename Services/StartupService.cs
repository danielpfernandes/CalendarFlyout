using Microsoft.Win32;

namespace CalendarFlyout.Services;

/// <summary>Registro de início por usuário: não exige administrador nem altera outras entradas.</summary>
public sealed class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CalendarFlyout";
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is string command && !string.IsNullOrWhiteSpace(command);
    }
    public static string BuildCommand(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"') ||
            !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use o caminho absoluto do executável instalado.");
        var command = $"\"{executable}\"";
        if (command.Length > 260) throw new InvalidOperationException("Mova o aplicativo para uma pasta com caminho mais curto para habilitar a inicialização.");
        return command;
    }
    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            existing?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }
        var executable = Path.Combine(AppContext.BaseDirectory, "CalendarFlyout.exe");
        if (!File.Exists(executable)) throw new InvalidOperationException("Execute a versão publicada do CalendarFlyout para ativar a inicialização.");
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, BuildCommand(executable), RegistryValueKind.String);
    }
}

using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace CalendarFlyout;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsMutex;
    private Forms.NotifyIcon? _tray;
    private Forms.ContextMenuStrip? _menu;
    private Icon? _icon;
    private MainWindow? _panel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, @"Local\CalendarFlyout", out _ownsMutex);
        if (!_ownsMutex) { Shutdown(); return; }

        // Sem StartupUri e sem Show(): nem janela, nem botão na barra de tarefas.
        _panel = new MainWindow();
        MainWindow = _panel;
        using (var resource = GetResourceStream(new Uri("pack://application:,,,/Assets/calendar.ico")).Stream)
        using (var source = new Icon(resource))
            _icon = (Icon)source.Clone();

        _menu = new Forms.ContextMenuStrip();
        _menu.Items.Add("Abrir agenda", null, (_, _) => Dispatcher.Invoke(_panel.ShowFlyout));
        _menu.Items.Add("Configurações", null, (_, _) => Dispatcher.Invoke(_panel.ShowSettings));
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add("Sair", null, (_, _) => Dispatcher.Invoke(Shutdown));
        _tray = new Forms.NotifyIcon
        {
            Icon = _icon, Text = "Google Calendar • Agenda", ContextMenuStrip = _menu, Visible = true
        };
        _tray.MouseClick += TrayMouseClick;
    }

    private void TrayMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
            Dispatcher.Invoke(() => _panel?.ShowFlyout());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.MouseClick -= TrayMouseClick;
            _tray.Visible = false;
            _tray.Dispose();
        }
        _menu?.Dispose();
        _icon?.Dispose();
        _panel?.Stop();
        if (_ownsMutex) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}

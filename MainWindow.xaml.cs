using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CalendarFlyout.Native;
using CalendarFlyout.Services;
using Google;
using Google.Apis.Auth.OAuth2.Responses;

namespace CalendarFlyout;

public partial class MainWindow : Window
{
    private readonly CalendarClient _calendar = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer;
    private readonly ThemeService _theme;
    private readonly SelectionStore _selectionStore = new();
    private readonly StartupService _startup = new();
    private List<CalendarChoice> _choices = new();
    private bool _choicesLoaded;
    private bool _busy;
    private bool _stopping;
    private Task _pending = Task.CompletedTask;

    public MainWindow()
    {
        InitializeComponent();
        _theme = new ThemeService(this);
        _theme.Apply();
        SourceInitialized += (_, _) => { WindowEffects.Initialize(this); _theme.Apply(); };
        // Só consulta em intervalos enquanto o painel estiver visível.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (_, _) => { if (SettingsPanel.Visibility != Visibility.Visible) Run(RefreshAsync); };
        UpdateButtons();
    }

    public void ShowFlyout()
    {
        if (_stopping) return;
        new WindowInteropHelper(this).EnsureHandle();
        _theme.Apply();
        WindowEffects.Place(this, false);
        Show();
        WindowEffects.Place(this, true);
        _timer.Start();
        if (SettingsPanel.Visibility != Visibility.Visible) Run(RefreshAsync);
    }

    private void Dismiss() { _timer.Stop(); Hide(); }
    private void Window_Deactivated(object? sender, EventArgs e) => Dismiss();
    private void Hide_Click(object sender, RoutedEventArgs e) => Dismiss();
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_stopping) { e.Cancel = true; Dismiss(); }
        base.OnClosing(e);
    }

    private void Connect_Click(object sender, RoutedEventArgs e) => Run(async token =>
    {
        StatusText.Text = "Conclua o login no navegador e reabra a agenda pela bandeja.";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await _calendar.ConnectAsync(timeout.Token);
        await RefreshAsync(token);
        // Nunca rouba foco nem reabre o painel após o retorno do navegador.
    });

    private void Refresh_Click(object sender, RoutedEventArgs e) => Run(RefreshAsync);
    private void Forget_Click(object sender, RoutedEventArgs e) => Run(async _ =>
    {
        await _calendar.ForgetAsync();
        await _selectionStore.SaveAsync(new CalendarSelection());
        _choicesLoaded = false;
        _choices.Clear();
        CalendarChoices.ItemsSource = null;
        DaysList.ItemsSource = null;
        StatusText.Text = "Login removido deste computador. Conecte novamente quando quiser.";
    });

    private async Task RefreshAsync(CancellationToken token)
    {
        if (!await _calendar.RestoreAsync())
        {
            StatusText.Text = "Conecte sua conta para ver a agenda.";
            return;
        }
        StatusText.Text = "Atualizando agenda…";
        var today = DateTime.Today;
        var selection = await _selectionStore.LoadAsync();
        var calendars = await _calendar.ListCalendarsAsync(token);
        var selected = selection.Apply(calendars);
        var result = await _calendar.LoadAsync(today, selected, token);
        if (_stopping) return;
        DaysList.ItemsSource = AgendaProjection.Build(result.Events, today, TimeZoneInfo.Local);
        StatusText.Text = selected.Count == 0
            ? "Nenhuma agenda selecionada/disponível. Abra Configurações para escolher."
            : $"Atualizado às {DateTime.Now:HH:mm} · {selected.Count} agenda(s) · {result.Events.Count} evento(s)";
        if (result.UnavailableCalendars.Count > 0)
            StatusText.Text += " · Não foi possível ler: " + string.Join(", ", result.UnavailableCalendars);
        if (selection.SelectedIds?.Any(id => calendars.All(c => c.Id != id)) == true)
            StatusText.Text += " · Uma agenda selecionada foi removida ou perdeu acesso. Confira Configurações.";
    }

    public void ShowSettings()
    {
        ShowFlyout();
        OpenSettings();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();
    private void OpenSettings()
    {
        AgendaContent.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        _choicesLoaded = false;
        _choices.Clear();
        CalendarChoices.ItemsSource = null;
        SettingsStatus.Text = "Carregando agendas…";
        try { StartupCheckBox.IsChecked = _startup.IsEnabled(); }
        catch (Exception) { StartupStatus.Text = "Não foi possível ler a configuração de inicialização."; }
        // A abertura pela bandeja pode já ter iniciado uma atualização. Aguarde-a, sem requisições concorrentes.
        _ = LoadSettingsAfterPendingAsync();
    }

    private async Task LoadSettingsAfterPendingAsync()
    {
        await _pending;
        if (_stopping || SettingsPanel.Visibility != Visibility.Visible) return;
        Run(async token =>
        {
            if (!await _calendar.RestoreAsync())
            {
                SettingsStatus.Text = "Volte e conecte sua conta Google para selecionar agendas.";
                return;
            }
            var calendars = await _calendar.ListCalendarsAsync(token);
            CalendarSelection selection;
            try { selection = await _selectionStore.LoadAsync(); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
            {
                selection = new();
            }
            var ids = selection.Apply(calendars).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            _choices = calendars.Select(c => new CalendarChoice { Id = c.Id, Name = c.Name, IsSelected = ids.Contains(c.Id) }).ToList();
            CalendarChoices.ItemsSource = _choices;
            _choicesLoaded = true;
            SettingsStatus.Text = calendars.Count == 0 ? "Nenhuma agenda com permissão de leitura encontrada."
                : "Marque as agendas que deseja exibir. A seleção será salva neste computador.";
        });
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e) => Run(async token =>
    {
        await _selectionStore.SaveAsync(new CalendarSelection { SelectedIds = _choices.Where(c => c.IsSelected).Select(c => c.Id).ToList() });
        // Limpa imediatamente para não mostrar eventos de agendas que acabaram de ser desmarcadas.
        DaysList.ItemsSource = null;
        CloseSettings();
        await RefreshAsync(token);
    });

    private void BackSettings_Click(object sender, RoutedEventArgs e) => CloseSettings();
    private void CloseSettings()
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        AgendaContent.Visibility = Visibility.Visible;
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var enabled = StartupCheckBox.IsChecked == true;
            _startup.SetEnabled(enabled);
            StartupStatus.Text = enabled ? "Ativado: iniciará oculto ao entrar no Windows." : "Inicialização automática desativada.";
        }
        catch (Exception ex)
        {
            StartupCheckBox.IsChecked = StartupCheckBox.IsChecked != true;
            StartupStatus.Text = ex is InvalidOperationException ? ex.Message : "Não foi possível alterar a inicialização. Verifique as permissões do Windows.";
        }
    }

    private void Run(Func<CancellationToken, Task> operation)
    {
        if (_busy || _stopping) return;
        _pending = ExecuteAsync(operation);
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> operation)
    {
        _busy = true;
        UpdateButtons();
        try { await operation(_lifetime.Token); }
        catch (OperationCanceledException)
        {
            if (!_stopping) StatusText.Text = "Operação cancelada ou login expirado. Tente novamente.";
        }
        catch (TokenResponseException)
        {
            DaysList.ItemsSource = null;
            StatusText.Text = "O Google recusou a autorização. Use Esquecer login e conecte novamente.";
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
        {
            DaysList.ItemsSource = null;
            StatusText.Text = "Sua autorização expirou. Use Esquecer login e conecte novamente.";
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            StatusText.Text = "Acesso negado: confira API/quota. Se faltar permissão, use Esquecer login e conecte novamente.";
        }
        catch (CryptographicException)
        {
            StatusText.Text = "Não foi possível proteger ou ler o login. Confira o perfil Windows; para remover um token inválido, use Esquecer login.";
        }
        catch (InvalidOperationException ex) { StatusText.Text = ex.Message; }
        catch (Exception)
        {
            // Não expõe tokens, respostas OAuth ou dados dos eventos em logs/mensagens.
            StatusText.Text = "Não foi possível atualizar. Confira a conexão e client_secret.json. Os dados exibidos podem estar desatualizados.";
        }
        finally
        {
            _busy = false;
            if (!_stopping)
            {
                if (SettingsPanel.Visibility == Visibility.Visible && !_choicesLoaded && _calendar.IsConnected)
                    SettingsStatus.Text = StatusText.Text;
                UpdateButtons();
            }
        }
    }

    private void UpdateButtons()
    {
        ConnectButton.IsEnabled = !_busy && !_calendar.IsConnected;
        ConnectButton.Visibility = _calendar.IsConnected ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.IsEnabled = !_busy && _calendar.IsConnected;
        // Disponível mesmo com um token corrompido ou configuração ausente.
        ForgetButton.IsEnabled = !_busy;
        CalendarChoices.IsEnabled = !_busy;
        SaveSettingsButton.IsEnabled = !_busy && _choicesLoaded;
    }

    public void Stop()
    {
        if (_stopping) return;
        _stopping = true;
        _timer.Stop();
        _theme.Dispose();
        _lifetime.Cancel();
        // Evita Dispose concorrente com uma requisição HTTP em andamento.
        if (_pending.IsCompleted) { _calendar.Dispose(); _lifetime.Dispose(); }
        else _ = _pending.ContinueWith(_ => { _calendar.Dispose(); _lifetime.Dispose(); }, TaskScheduler.Default);
    }
}

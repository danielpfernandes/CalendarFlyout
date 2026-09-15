using System.Text.Json;
using System.Net;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace CalendarFlyout.Services;

public sealed record CalendarEvent(Event Event, string CalendarId, string CalendarName);
public sealed record CalendarLoadResult(List<CalendarEvent> Events, List<string> UnavailableCalendars);

public sealed class CalendarClient : IDisposable
{
    // Outra chave exige consentimento para o novo escopo sem reutilizar um token antigo insuficiente.
    private const string UserKey = "default-user-calendars-v2";
    private static readonly string[] Scopes = { CalendarService.Scope.CalendarEventsReadonly, CalendarService.Scope.CalendarCalendarlistReadonly };
    private readonly EncryptedDataStore _store;
    public CalendarClient(CalendarService? service = null, EncryptedDataStore? store = null)
    {
        _service = service;
        _store = store ?? new EncryptedDataStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalendarFlyout", "Tokens"));
    }
    private GoogleAuthorizationCodeFlow? _flow;
    private CalendarService? _service;
    public bool IsConnected => _service is not null;

    private static ClientSecrets LoadSecrets()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "client_secret.json");
        if (!File.Exists(path))
            throw new InvalidOperationException("Coloque client_secret.json (OAuth para aplicativo de computador) ao lado do executável.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("installed", out var installed))
            throw new InvalidOperationException("Use uma credencial OAuth do tipo Aplicativo para computador.");
        return new ClientSecrets
        {
            ClientId = installed.GetProperty("client_id").GetString(),
            ClientSecret = installed.GetProperty("client_secret").GetString()
        };
    }

    public async Task<bool> RestoreAsync()
    {
        if (IsConnected) return true;
        var token = await _store.GetAsync<TokenResponse>(UserKey);
        if (token is null) return false;
        _flow?.Dispose();
        _flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = LoadSecrets(), Scopes = Scopes, DataStore = _store
        });
        SetCredential(new UserCredential(_flow, UserKey, token));
        return true;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        // Navegador padrão + callback loopback gerenciados pela biblioteca oficial.
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            LoadSecrets(), Scopes, UserKey, cancellationToken, _store);
        _service?.Dispose();
        _service = null;
        _flow?.Dispose();
        _flow = credential.Flow as GoogleAuthorizationCodeFlow;
        SetCredential(credential);
        await _store.DeleteAsync<TokenResponse>("default-user");
    }

    private void SetCredential(UserCredential credential)
    {
        _service = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential, ApplicationName = "CalendarFlyout"
        });
        _service.HttpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<List<CalendarOption>> ListCalendarsAsync(CancellationToken cancellationToken)
    {
        var service = _service ?? throw new InvalidOperationException("Conecte sua conta Google.");
        var calendars = new List<CalendarOption>();
        string? page = null;
        do
        {
            var request = service.CalendarList.List();
            request.ShowHidden = true;
            request.ShowDeleted = false;
            request.MinAccessRole = CalendarListResource.ListRequest.MinAccessRoleEnum.Reader;
            request.MaxResults = 250;
            request.PageToken = page;
            var response = await request.ExecuteAsync(cancellationToken);
            foreach (var calendar in response.Items ?? Array.Empty<Google.Apis.Calendar.v3.Data.CalendarListEntry>())
            {
                if (string.IsNullOrWhiteSpace(calendar.Id)) continue;
                calendars.Add(new CalendarOption(calendar.Id,
                    calendar.SummaryOverride ?? calendar.Summary ?? calendar.Id, calendar.Primary == true));
            }
            page = response.NextPageToken;
        } while (!string.IsNullOrEmpty(page));
        return calendars.DistinctBy(c => c.Id).OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name).ToList();
    }

    public async Task<CalendarLoadResult> LoadAsync(DateTime firstDay, IReadOnlyList<CalendarOption> calendars,
        CancellationToken cancellationToken)
    {
        var service = _service ?? throw new InvalidOperationException("Conecte sua conta Google.");
        var start = AgendaProjection.LocalBoundary(firstDay, TimeZoneInfo.Local);
        var end = AgendaProjection.LocalBoundary(firstDay.AddDays(4), TimeZoneInfo.Local);
        var events = new List<CalendarEvent>();
        var unavailable = new List<string>();
        foreach (var calendar in calendars)
        {
            // Só publica uma agenda depois de ler todas as páginas dela.
            var calendarEvents = new List<CalendarEvent>();
            string? page = null;
            try
            {
                do
                {
                    var request = service.Events.List(calendar.Id);
                    request.TimeMinDateTimeOffset = start;
                    request.TimeMaxDateTimeOffset = end;
                    request.TimeZone = "UTC";
                    request.SingleEvents = true;
                    request.ShowDeleted = false;
                    request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
                    request.MaxResults = 2500;
                    request.PageToken = page;
                    var response = await request.ExecuteAsync(cancellationToken);
                    if (response.Items is not null)
                        calendarEvents.AddRange(response.Items.Select(e => new CalendarEvent(e, calendar.Id, calendar.Name)));
                    page = response.NextPageToken;
                } while (!string.IsNullOrEmpty(page));
                events.AddRange(calendarEvents);
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                // Uma agenda removida/restrita não apaga os resultados das outras; a UI avisa a falha.
                unavailable.Add(calendar.Name);
            }
        }
        return new CalendarLoadResult(events, unavailable);
    }

    public async Task ForgetAsync()
    {
        Dispose();
        await _store.ClearAsync();
    }

    public void Dispose()
    {
        _service?.Dispose(); _service = null;
        _flow?.Dispose(); _flow = null;
    }
}

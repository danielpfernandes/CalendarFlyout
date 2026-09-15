using System.IO;
using System.Net;
using System.Net.Http;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Http;
using CalendarFlyout.Services;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3.Data;

var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

var today = new DateTime(2026, 9, 15);
Event Timed(string title, string start, string end) => new()
{
    Summary = title,
    Start = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse(start) },
    End = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse(end) }
};
var zone = TimeZoneInfo.CreateCustomTimeZone("Test -03", TimeSpan.FromHours(-3), "Test -03", "Test -03");
var entries = new List<Event>
{
    new() { Summary = "Férias", Start = new() { Date = "2026-09-14" }, End = new() { Date = "2026-09-17" } },
    Timed("Virada", "2026-09-16T02:30:00Z", "2026-09-16T03:30:00Z"),
    Timed("À meia-noite", "2026-09-15T22:00:00-03:00", "2026-09-16T00:00:00-03:00"),
    Timed("Último dia", "2026-09-18T23:00:00-03:00", "2026-09-19T00:00:00-03:00"),
    Timed("Fora", "2026-09-19T00:00:00-03:00", "2026-09-19T01:00:00-03:00"),
    new() { Summary = "Cancelado", Status = "cancelled", Start = new() { Date = "2026-09-15" }, End = new() { Date = "2026-09-16" } },
    Timed("", "2026-09-15T10:00:00-03:00", "2026-09-15T11:00:00-03:00")
};
var days = AgendaProjection.Build(entries, today, zone);
Check(days.Count == 4, "Hoje e próximos três dias");
Check(days[0].Events[0].Title == "Férias", "Dia inteiro antes dos eventos com horário");
Check(days[1].Events.Any(x => x.Title == "Férias") && !days[2].Events.Any(x => x.Title == "Férias"), "Fim exclusivo de all-day");
Check(days[0].Events.Any(x => x.Title == "Virada") && days[1].Events.Any(x => x.Title == "Virada"), "Evento atravessando meia-noite em dois dias");
Check(days[0].Events.Single(x => x.Title == "Virada").TimeLabel == "15/09 23:30 – 16/09 00:30", "Conversão UTC para horário local");
Check(!days[1].Events.Any(x => x.Title == "À meia-noite"), "Fim à meia-noite não ocupa dia seguinte");
Check(days[3].Events.Any(x => x.Title == "Último dia") && days.All(d => d.Events.All(x => x.Title != "Fora")), "Limites da janela de quatro dias");
Check(days.All(d => d.Events.All(x => x.Title != "Cancelado")), "Eventos cancelados excluídos");
Check(days[0].Events.Any(x => x.Title == "(Sem título)"), "Título ausente");
Check(days[2].EmptyVisibility == System.Windows.Visibility.Visible, "Dia vazio");
var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
Check((AgendaProjection.LocalBoundary(new DateTime(2026, 3, 9), eastern) - AgendaProjection.LocalBoundary(new DateTime(2026, 3, 8), eastern)).TotalHours == 23, "Limites respeitam mudança de horário de verão");

var folder = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) is { } directory ? directory : Path.Combine(Path.GetTempPath(), "CalendarFlyout-tests-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(folder);
var available = new List<CalendarOption>
{
    new("main@example.com", "Pessoal", true), new("work@example.com", "Trabalho", false),
    new("holidays", "Feriados", false)
};
Check(new CalendarSelection().Apply(available).Select(c => c.Id).SequenceEqual(new[] { "main@example.com" }), "Primeiro uso preserva somente a principal");
var selectionStore = new SelectionStore(Path.Combine(folder, "selection.json"));
await selectionStore.SaveAsync(new CalendarSelection { SelectedIds = new() { "work@example.com", "holidays" } });
var savedSelection = await new SelectionStore(Path.Combine(folder, "selection.json")).LoadAsync();
Check(savedSelection.Apply(available).Select(c => c.Name).SequenceEqual(new[] { "Trabalho", "Feriados" }), "Seleção persiste e filtra por ID");
Check(savedSelection.Apply(available.Take(1)).Count == 0, "Agenda removida não reativa a principal");
await selectionStore.SaveAsync(new CalendarSelection { SelectedIds = new() });
Check((await selectionStore.LoadAsync()).Apply(available).Count == 0, "Nenhuma agenda selecionada persiste sem fallback");
await selectionStore.SaveAsync(new CalendarSelection());
Check((await selectionStore.LoadAsync()).Apply(available).Single().IsPrimary, "Reset para troca de conta restaura padrão");
var sameEvent = Timed("Reunião", "2026-09-15T10:00:00-03:00", "2026-09-15T11:00:00-03:00");
var combined = AgendaProjection.Build(new[] {
    new CalendarEvent(sameEvent, "one", "Pessoal"), new CalendarEvent(sameEvent, "two", "Trabalho")
}, today, zone);
Check(combined[0].Events.Select(e => e.CalendarName).SequenceEqual(new[] { "Pessoal", "Trabalho" }), "Eventos de agendas distintas mantêm origem sem colidir");
Check(StartupService.BuildCommand(@"C:\Users\Nome Completo\CalendarFlyout.exe") == "\"C:\\Users\\Nome Completo\\CalendarFlyout.exe\"", "Inicialização protege caminho com espaços");
var relativeRejected = false;
try { StartupService.BuildCommand("CalendarFlyout.exe"); } catch (ArgumentException) { relativeRejected = true; }
Check(relativeRejected, "Inicialização rejeita caminho relativo");

var handler = new CalendarHandler();
using (var service = new CalendarService(new BaseClientService.Initializer { HttpClientFactory = new FakeFactory(handler), ApplicationName = "CalendarFlyout.Tests" }))
using (var client = new CalendarClient(service, new EncryptedDataStore(Path.Combine(folder, "fake-oauth"))))
{
    var calendars = await client.ListCalendarsAsync(CancellationToken.None);
    Check(calendars.Count == 3 && calendars.Any(c => c.Id == "holidays"), "API lê todas as páginas da lista de agendas");
    Check(handler.Uris.Take(2).All(u => u.Query.Contains("showHidden=true") && u.Query.Contains("minAccessRole=reader")), "API inclui agendas ocultas com leitura de eventos");
    var selected = new CalendarSelection { SelectedIds = new() { "work@example.com" } }.Apply(calendars);
    var result = await client.LoadAsync(today, selected, CancellationToken.None);
    Check(result.Events.Count == 2 && result.Events.All(e => e.CalendarName == "Trabalho"), "API agrega todas as páginas dos eventos com origem");
    Check(handler.Uris.Skip(2).All(u => Uri.UnescapeDataString(u.AbsolutePath).Contains("work@example.com")), "API não consulta eventos de agendas desmarcadas");
    var count = handler.Uris.Count;
    Check((await client.LoadAsync(today, Array.Empty<CalendarOption>(), CancellationToken.None)).Events.Count == 0 && handler.Uris.Count == count, "Seleção vazia não consulta eventos");
    var partial = await client.LoadAsync(today, calendars.Where(c => !c.IsPrimary).ToList(), CancellationToken.None);
    Check(partial.Events.Count == 2 && partial.UnavailableCalendars.SequenceEqual(new[] { "Feriados" }), "Agenda indisponível preserva outras e informa falha");
}
if (args.Contains("--skip-dpapi"))
{
    Console.WriteLine($"{passed} verificações passaram. DPAPI não executado (--skip-dpapi).");
    return;
}
var store = new EncryptedDataStore(folder);
const string key = "roundtrip";
var token = new TokenResponse { AccessToken = "fake-access-secret", RefreshToken = "fake-refresh-secret", TokenType = "Bearer" };
await store.StoreAsync(key, token);
var bytes = await File.ReadAllBytesAsync(Directory.GetFiles(folder, "*.bin").Single());
Check(!System.Text.Encoding.UTF8.GetString(bytes).Contains("fake-access-secret"), "Token não persistido em texto puro");
var restored = await new EncryptedDataStore(folder).GetAsync<TokenResponse>(key);
Check(restored.AccessToken == token.AccessToken && restored.RefreshToken == token.RefreshToken, "DPAPI recupera access e refresh token em nova instância");
await store.StoreAsync(key, new TokenResponse { AccessToken = "updated" });
Check((await store.GetAsync<TokenResponse>(key)).AccessToken == "updated", "Atualização atômica do token");
await store.DeleteAsync<TokenResponse>(key);
Check(await store.GetAsync<TokenResponse>(key) is null, "Exclusão do token");
await store.StoreAsync(key, token);
await store.ClearAsync();
Check(await store.GetAsync<TokenResponse>(key) is null, "Limpeza do login");
Console.WriteLine($"{passed} verificações passaram.");


sealed class FakeFactory(CalendarHandler handler) : IHttpClientFactory
{
    public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args) =>
        new(new ConfigurableMessageHandler(handler));
}
sealed class CalendarHandler : HttpMessageHandler
{
    public List<Uri> Uris { get; } = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri!;
        Uris.Add(uri);
        string json;
        var status = HttpStatusCode.OK;
        if (uri.AbsolutePath.EndsWith("calendarList"))
            json = uri.Query.Contains("pageToken=next")
                ? """{"items":[{"id":"holidays","summary":"Feriados","accessRole":"reader"}]}"""
                : """{"nextPageToken":"next","items":[{"id":"main@example.com","summary":"Pessoal","primary":true,"accessRole":"owner"},{"id":"work@example.com","summary":"Trabalho","accessRole":"reader"}]}""";
        else if (uri.AbsolutePath.Contains("holidays"))
        {
            status = HttpStatusCode.NotFound;
            json = """{"error":{"code":404,"message":"Not found"}}""";
        }
        else
            json = uri.Query.Contains("pageToken=next")
                ? """{"items":[{"id":"b","summary":"Segundo","start":{"date":"2026-09-16"},"end":{"date":"2026-09-17"}}]}"""
                : """{"nextPageToken":"next","items":[{"id":"a","summary":"Primeiro","start":{"date":"2026-09-15"},"end":{"date":"2026-09-16"}}]}""";
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
    }
}

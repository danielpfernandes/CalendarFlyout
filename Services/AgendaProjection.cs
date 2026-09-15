using System.Globalization;
using System.Windows;
using Google.Apis.Calendar.v3.Data;

namespace CalendarFlyout.Services;

public sealed record AgendaItem(string Title, string TimeLabel, DateTimeOffset SortTime, bool AllDay, string CalendarName = "");
public sealed record AgendaDay(string Label, List<AgendaItem> Events)
{
    public Visibility EmptyVisibility => Events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}

public static class AgendaProjection
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static DateTimeOffset LocalBoundary(DateTime day, TimeZoneInfo zone)
    {
        var wall = DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified);
        // Há zonas que avançam o relógio à meia-noite. Use o primeiro instante válido.
        while (zone.IsInvalidTime(wall)) wall = wall.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(wall) ? zone.GetAmbiguousTimeOffsets(wall).Max() : zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset);
    }

    public static List<AgendaDay> Build(IEnumerable<Event> source, DateTime today, TimeZoneInfo zone)
    {
        return Build(source.Select(e => new CalendarEvent(e, "primary", "Agenda principal")), today, zone);
    }

    public static List<AgendaDay> Build(IEnumerable<CalendarEvent> source, DateTime today, TimeZoneInfo zone)
    {
        var events = source.Where(x => x.Event.Status != "cancelled" && x.Event.Start is not null && x.Event.End is not null).ToList();
        var days = new List<AgendaDay>();
        for (var i = 0; i < 4; i++)
        {
            var day = today.Date.AddDays(i);
            var min = LocalBoundary(day, zone);
            var max = LocalBoundary(day.AddDays(1), zone);
            var items = new List<AgendaItem>();
            foreach (var entry in events)
            {
                var e = entry.Event;
                var title = string.IsNullOrWhiteSpace(e.Summary) ? "(Sem título)" : e.Summary;
                if (!string.IsNullOrEmpty(e.Start.Date))
                {
                    // A data final do Google é exclusiva; datas all-day não mudam de fuso.
                    if (DateTime.TryParseExact(e.Start.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
                        && DateTime.TryParseExact(e.End.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)
                        && day >= from && day < to)
                        items.Add(new AgendaItem(title, "Dia inteiro", min, true, entry.CalendarName));
                    continue;
                }
                var start = e.Start.DateTimeDateTimeOffset;
                var end = e.End.DateTimeDateTimeOffset;
                if (start is null || end is null) continue;
                var overlaps = start < max && (end > min || (end == start && start >= min));
                if (!overlaps) continue;
                var localStart = TimeZoneInfo.ConvertTime(start.Value, zone);
                var localEnd = TimeZoneInfo.ConvertTime(end.Value, zone);
                var format = localStart.Date == localEnd.Date ? "HH:mm" : "dd/MM HH:mm";
                var label = $"{localStart.ToString(format, Culture)} – {localEnd.ToString(format, Culture)}";
                items.Add(new AgendaItem(title, label, start.Value, false, entry.CalendarName));
            }
            var prefix = i == 0 ? "Hoje" : i == 1 ? "Amanhã" : day.ToString("dddd", Culture);
            days.Add(new AgendaDay($"{prefix} · {day:dd/MM}", items.OrderByDescending(x => x.AllDay).ThenBy(x => x.SortTime).ToList()));
        }
        return days;
    }
}

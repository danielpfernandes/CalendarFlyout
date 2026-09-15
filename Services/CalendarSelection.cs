using System.Text.Json;

namespace CalendarFlyout.Services;

public sealed record CalendarOption(string Id, string Name, bool IsPrimary);
public sealed class CalendarChoice
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsSelected { get; set; }
}

/// <summary>Null preserva o padrão (principal); uma lista vazia significa nenhuma agenda.</summary>
public sealed class CalendarSelection
{
    public List<string>? SelectedIds { get; set; }
    public List<CalendarOption> Apply(IEnumerable<CalendarOption> calendars) => calendars
        .Where(c => SelectedIds is null ? c.IsPrimary : SelectedIds.Contains(c.Id, StringComparer.Ordinal))
        .ToList();
}

public sealed class SelectionStore
{
    private readonly string _path;
    public SelectionStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CalendarFlyout", "selection.json");
    public async Task<CalendarSelection> LoadAsync()
    {
        if (!File.Exists(_path)) return new();
        return JsonSerializer.Deserialize<CalendarSelection>(await File.ReadAllTextAsync(_path))
            ?? throw new InvalidDataException("Seleção de agendas inválida. Salve novamente em Configurações.");
    }
    public async Task SaveAsync(CalendarSelection value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

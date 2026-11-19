using System.Text.Json;

namespace DeskPal.Client.Ui;

/// <summary>A message from the page: <c>{ id, type, ...args }</c>.</summary>
public sealed record WebCommand(int? Id, string Type, JsonElement Args)
{
    public static WebCommand? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Clone();
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type is null)
            {
                return null;
            }

            int? id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : null;
            return new WebCommand(id, type, root);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string String(string name) =>
        Args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    public string? OptionalString(string name) =>
        Args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public bool Bool(string name) => Args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    public Guid Guid(string name) => System.Guid.TryParse(String(name), out var id) ? id : throw new FormatException(UiLanguage.Format(Strings.Error_InvalidId, name));

    /// <summary>Distinct hours of the day, sorted; null when missing or empty. Values outside 0 to 23 are dropped.</summary>
    public IReadOnlyList<int>? HourList(string name)
    {
        if (!Args.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var hours = value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var h) && h is >= 0 and < 24)
            .Select(e => e.GetInt32())
            .Distinct()
            .Order()
            .ToList();
        return hours.Count == 0 ? null : hours;
    }

    public List<Guid> GuidList(string name) =>
        Args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(e => System.Guid.TryParse(e.GetString(), out var id) ? id : throw new FormatException(UiLanguage.Format(Strings.Error_InvalidId, name))).ToList()
            : throw new FormatException(UiLanguage.Format(Strings.Error_InvalidId, name));
}

/// <summary>Reply to a <see cref="WebCommand"/>. Error text is shown to the user as is.</summary>
public sealed record CommandResult(bool Ok, string? Error = null, object? Data = null)
{
    public static CommandResult Success { get; } = new(true);

    public static CommandResult Fail(string error) => new(false, error);
}
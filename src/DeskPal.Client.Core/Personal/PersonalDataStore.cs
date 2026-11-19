using System.Text.Json;
using DeskPal.Shared;

namespace DeskPal.Client.Core.Personal;

/// <summary>This computer's copy of each account's personal data, one file per account.</summary>
public sealed class PersonalDataStore(string directory)
{
    private readonly Lock _lock = new();

    /// <returns>null when this account has nothing saved here yet.</returns>
    public PersonalData? Load(Guid userId)
    {
        lock (_lock)
        {
            var path = PathOf(userId);
            try
            {
                return File.Exists(path) && JsonSerializer.Deserialize<PersonalData>(File.ReadAllText(path), DeskPalJson.Options) is { } data
                    ? data.Sanitize()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public void Save(Guid userId, PersonalData data)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(directory);
            var path = PathOf(userId);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, DeskPalJson.Options));
            File.Move(temp, path, overwrite: true);
        }
    }

    private string PathOf(Guid userId) => Path.Combine(directory, $"{userId:N}.json");
}
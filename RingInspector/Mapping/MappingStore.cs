using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RingInspector.Mapping;

/// <summary>Persists mapping rules to %AppData%\RingInspector\mappings.json.</summary>
public static class MappingStore
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RingInspector", "mappings.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static List<MappingRule> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<MappingRule>>(File.ReadAllText(FilePath), Options) ?? []
                : [];
        }
        catch (Exception)
        {
            return []; // unreadable file: start fresh rather than crash
        }
    }

    public static void Save(IEnumerable<MappingRule> rules)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(rules, Options));
    }
}

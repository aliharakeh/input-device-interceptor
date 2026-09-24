using System.IO;
using System.Text.Json;

namespace InputDeviceInterceptor.Mapping;

/// <summary>A touch collection this app has hidden from Windows and reads itself.</summary>
public sealed record BlockedCollection(
    string DeviceKey,
    string DeviceName,
    string InterfacePath,
    string InstanceId,
    Guid? ContainerId);

public sealed class TouchBlockState
{
    public List<BlockedCollection> Collections { get; set; } = [];
    /// <summary>True when HidHide's global hiding was off and this app turned it on (so it turns it back off).</summary>
    public bool CloakEnabledByApp { get; set; }
    /// <summary>Whether the app reads the hidden touch channel itself (needed for touch mappings).</summary>
    public bool ReadWhileBlocked { get; set; } = true;
}

/// <summary>Persists touch blocking to %AppData%\InputDeviceInterceptor\touch-block.json.</summary>
public static class TouchBlockStore
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InputDeviceInterceptor", "touch-block.json");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static TouchBlockState Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<TouchBlockState>(File.ReadAllText(FilePath), Options) ?? new()
                : new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    public static void Save(TouchBlockState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(state, Options));
    }
}

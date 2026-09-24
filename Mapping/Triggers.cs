using InputDeviceInterceptor.Input;

namespace InputDeviceInterceptor.Mapping;

/// <summary>
/// A trigger identifies an input that can be mapped, as a stable string id:
/// <c>key:AF</c>, <c>hid:000C:00E9</c>, <c>mouse:0001</c>, <c>wheel:up</c>, <c>touch:up</c>, <c>drag:left</c>…
/// </summary>
public static class Triggers
{
    /// <summary>Consumer usages that Windows turns into virtual-key presses (and that a keyboard hook can therefore block).</summary>
    public static readonly IReadOnlyDictionary<ushort, ushort> ConsumerToVk = new Dictionary<ushort, ushort>
    {
        [0xE9] = 0xAF, // Volume Up
        [0xEA] = 0xAE, // Volume Down
        [0xE2] = 0xAD, // Mute
        [0xCD] = 0xB3, // Play/Pause
        [0xB5] = 0xB0, // Next Track
        [0xB6] = 0xB1, // Previous Track
        [0xB7] = 0xB2, // Stop
    };

    static readonly Dictionary<ushort, string> MouseButtonNames = new()
    {
        [0x0001] = "Left click",
        [0x0004] = "Right click",
        [0x0010] = "Middle click",
        [0x0040] = "Back button (X1)",
        [0x0100] = "Forward button (X2)",
    };

    public static string Key(ushort vk) => $"key:{vk:X2}";
    public static string Hid(ushort page, ushort usage) => $"hid:{page:X4}:{usage:X4}";
    public static string Mouse(ushort buttonFlag) => $"mouse:{buttonFlag:X4}";
    public static string Wheel(bool horizontal, bool positive) =>
        horizontal ? (positive ? "hwheel:right" : "hwheel:left") : (positive ? "wheel:up" : "wheel:down");
    /// <param name="gesture">tap, up, down, left or right</param>
    public static string Gesture(bool touch, string gesture) => $"{(touch ? "touch" : "drag")}:{gesture}";

    public static string Describe(string id)
    {
        var parts = id.Split(':');
        switch (parts[0])
        {
            case "key":
                return $"Key: {KeyTable.Describe(Convert.ToUInt16(parts[1], 16)).Name}";
            case "hid":
                ushort page = Convert.ToUInt16(parts[1], 16), usage = Convert.ToUInt16(parts[2], 16);
                return $"{UsageTable.Describe(page, usage).Name} ({UsageTable.PageName(page)})";
            case "mouse":
                return MouseButtonNames.GetValueOrDefault(Convert.ToUInt16(parts[1], 16), id);
            case "wheel":
            case "hwheel":
                return $"Mouse wheel {parts[1]}";
            case "touch":
                return parts[1] == "tap" ? "Touch: tap" : $"Touch: swipe {parts[1]}";
            case "drag":
                return $"Mouse: drag {parts[1]}";
            default:
                return id;
        }
    }

    /// <summary>The virtual key whose default action a keyboard hook can suppress for this trigger, if any.</summary>
    public static ushort? BlockableVk(string id)
    {
        var parts = id.Split(':');
        if (parts[0] == "key")
            return Convert.ToUInt16(parts[1], 16);
        if (parts[0] == "hid" && Convert.ToUInt16(parts[1], 16) == 0x0C
            && ConsumerToVk.TryGetValue(Convert.ToUInt16(parts[2], 16), out var vk))
            return vk;
        return null;
    }

    public static bool CanBlock(string id) => BlockableVk(id) is not null;

    public static string WhyCantBlock(string id)
    {
        var parts = id.Split(':');
        return parts[0] switch
        {
            "touch" => "Touch is blocked for the whole device — use \"Block touch from Windows\" above",
            "hid" when UsageTable.HasDefaultAction(Convert.ToUInt16(parts[1], 16), Convert.ToUInt16(parts[2], 16))
                => "Windows handles this input internally; apps can't block it",
            "hid" => "Windows does nothing with this input, so there's nothing to block",
            _ => "Blocking mouse input isn't supported yet",
        };
    }

    /// <summary>Triggers worth offering for a device, based on the kinds of collections it exposes.</summary>
    public static IEnumerable<string> Suggested(IEnumerable<string> collectionKinds)
    {
        var kinds = collectionKinds.ToList();
        if (kinds.Any(k => k.StartsWith("Touch Screen")))
            foreach (var g in new[] { "up", "down", "left", "right", "tap" })
                yield return Gesture(touch: true, g);
        if (kinds.Any(k => k.StartsWith("Consumer")))
            foreach (ushort usage in new ushort[] { 0xE9, 0xEA, 0xE2, 0xCD, 0xB5, 0xB6 })
                yield return Hid(0x0C, usage);
        if (kinds.Contains("Mouse"))
        {
            yield return Wheel(false, true);
            yield return Wheel(false, false);
            foreach (var flag in MouseButtonNames.Keys)
                yield return Mouse(flag);
        }
    }
}

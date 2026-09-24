using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using InputDeviceInterceptor.Input;

namespace InputDeviceInterceptor.Mapping;

public enum MappingOperation
{
    BlockOnly,
    ScrollUp,
    ScrollDown,
    Keyboard,
}

public sealed record OperationOption(string Name, MappingOperation Value);

public sealed record KeyOption(string Name, ushort Vk);

public sealed record TriggerOption(string Id, string Label);

/// <summary>"When this device sends <see cref="TriggerId"/>, do <see cref="Operation"/>."</summary>
public sealed class MappingRule : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary><c>bt:{containerId}</c> for a Bluetooth device, <c>dev:{path}</c> for a single input collection.</summary>
    public string DeviceKey { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string TriggerId { get; set; } = "";

    bool _enabled = true, _blockDefault, _ctrl, _alt, _shift, _win;
    MappingOperation _operation = MappingOperation.ScrollDown;
    double _scrollAmount = 3;
    ushort _keyVk = 0x20;

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public bool BlockDefault { get => _blockDefault; set => Set(ref _blockDefault, value); }
    public double ScrollAmount { get => _scrollAmount; set => Set(ref _scrollAmount, value); }
    public bool Ctrl { get => _ctrl; set => Set(ref _ctrl, value); }
    public bool Alt { get => _alt; set => Set(ref _alt, value); }
    public bool Shift { get => _shift; set => Set(ref _shift, value); }
    public bool Win { get => _win; set => Set(ref _win, value); }
    public ushort KeyVk { get => _keyVk; set => Set(ref _keyVk, value); }

    public MappingOperation Operation
    {
        get => _operation;
        set
        {
            Set(ref _operation, value);
            Raise(nameof(IsScroll));
            Raise(nameof(IsKeyboard));
        }
    }

    [JsonIgnore] public bool IsScroll => Operation is MappingOperation.ScrollUp or MappingOperation.ScrollDown;
    [JsonIgnore] public bool IsKeyboard => Operation == MappingOperation.Keyboard;
    [JsonIgnore] public string TriggerLabel => Triggers.Describe(TriggerId);
    [JsonIgnore] public bool CanBlock => Triggers.CanBlock(TriggerId);
    [JsonIgnore] public string BlockHint => CanBlock
        ? "Stop Windows' default action for this input (only for this device)"
        : Triggers.WhyCantBlock(TriggerId);

    [JsonIgnore]
    public string Summary => Operation switch
    {
        MappingOperation.ScrollUp => $"Scroll up {ScrollAmount:0.##}",
        MappingOperation.ScrollDown => $"Scroll down {ScrollAmount:0.##}",
        MappingOperation.Keyboard => KeyComboText,
        _ => "Nothing",
    };

    [JsonIgnore]
    public string KeyComboText
    {
        get
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            parts.Add(MappingOptions.KeyName(KeyVk));
            return string.Join("+", parts);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        Raise(name);
        Raise(nameof(Summary));
    }

    void Raise(string name) => PropertyChanged?.Invoke(this, new(name));
}

public static class MappingOptions
{
    public static IReadOnlyList<OperationOption> Operations { get; } =
    [
        new("Scroll down", MappingOperation.ScrollDown),
        new("Scroll up", MappingOperation.ScrollUp),
        new("Keyboard shortcut", MappingOperation.Keyboard),
        new("Nothing (just block default)", MappingOperation.BlockOnly),
    ];

    public static IReadOnlyList<KeyOption> Keys { get; } = BuildKeys();

    public static string KeyName(ushort vk) => Keys.FirstOrDefault(k => k.Vk == vk)?.Name ?? KeyTable.Describe(vk).Name;

    static List<KeyOption> BuildKeys()
    {
        var keys = new List<KeyOption>
        {
            new("Space", 0x20), new("Enter", 0x0D), new("Escape", 0x1B), new("Tab", 0x09),
            new("Backspace", 0x08), new("Delete", 0x2E), new("Insert", 0x2D),
            new("Up arrow", 0x26), new("Down arrow", 0x28), new("Left arrow", 0x25), new("Right arrow", 0x27),
            new("Page Up", 0x21), new("Page Down", 0x22), new("Home", 0x24), new("End", 0x23),
        };
        for (char c = 'A'; c <= 'Z'; c++)
            keys.Add(new(c.ToString(), c));
        for (char c = '0'; c <= '9'; c++)
            keys.Add(new(c.ToString(), c));
        for (int f = 1; f <= 12; f++)
            keys.Add(new($"F{f}", (ushort)(0x6F + f)));
        keys.AddRange(
        [
            new("Volume Up", 0xAF), new("Volume Down", 0xAE), new("Volume Mute", 0xAD),
            new("Play/Pause", 0xB3), new("Next Track", 0xB0), new("Previous Track", 0xB1),
            new("Browser Back", 0xA6), new("Browser Forward", 0xA7), new("Print Screen", 0x2C),
        ]);
        return keys;
    }
}

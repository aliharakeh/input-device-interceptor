using System.ComponentModel;
using InputDeviceInterceptor.Native;

namespace InputDeviceInterceptor.Input;

/// <summary>One Raw Input device (a HID top-level collection) plus the parsing state we keep for it.</summary>
public sealed class DeviceInfo : INotifyPropertyChanged
{
    public static readonly DeviceInfo Injected = new()
    {
        Name = "Injected input",
        Kind = "No device handle (SendInput / remote desktop / virtual)",
    };

    public nint Handle { get; init; }
    public string Path { get; init; } = "";
    string _name = "";
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            PropertyChanged?.Invoke(this, new(nameof(Name)));
            PropertyChanged?.Invoke(this, new(nameof(Display)));
        }
    }
    /// <summary>True when the device gave no product string and <see cref="Name"/> is a placeholder.</summary>
    public bool NameIsFallback { get; init; }
    public string? Manufacturer { get; init; }
    /// <summary>What the collection is: Keyboard, Mouse, Consumer Control, Touch Screen...</summary>
    public string Kind { get; init; } = "";
    public string? VidPid { get; init; }
    public bool IsBluetooth { get; init; }
    /// <summary>PnP container (physical device); shared by all collections of the same Bluetooth device.</summary>
    public Guid? ContainerId { get; set; }

    public string Display => $"{Name} · {Kind}";
    public string Tooltip => $"{Name}\n{Kind}\nManufacturer: {Manufacturer ?? "?"}\nVID/PID: {VidPid ?? "?"}\n{Path}";

    int _eventCount;
    public int EventCount
    {
        get => _eventCount;
        set { _eventCount = value; PropertyChanged?.Invoke(this, new(nameof(EventCount))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // --- HID parsing state ---
    /// <summary>HID preparsed data (unmanaged, lives as long as the device entry); 0 when unavailable.</summary>
    internal nint Preparsed;
    internal NativeMethods.HIDP_VALUE_CAPS[] ValueCaps = [];
    internal uint MaxUsages;
    internal readonly Dictionary<byte, HashSet<uint>> PressedByReport = [];
    internal readonly Dictionary<(byte Report, ushort Page, ushort Usage, ushort Link), int> LastValues = [];
    internal int TouchX, TouchY, TouchStartX, TouchStartY, TouchRange = 1000;

    // --- keyboard / mouse state ---
    internal readonly HashSet<ushort> PressedKeys = [];
    internal bool Dragging;
    internal int DragDx, DragDy, AbsX, AbsY, DragStartAbsX, DragStartAbsY;
}

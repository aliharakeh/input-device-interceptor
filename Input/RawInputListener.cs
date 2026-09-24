using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using RingInspector.Mapping;
using static RingInspector.Native.NativeMethods;

namespace RingInspector.Input;

/// <summary>
/// Listens to keyboards, mice, media-key (consumer), touch/digitizer, gamepad and telephony HID collections
/// through the Win32 Raw Input API, in the background (RIDEV_INPUTSINK), and turns every report into
/// <see cref="InputEvent"/>s. Raw Input only observes: Windows still performs each input's normal action.
/// </summary>
public sealed unsafe partial class RawInputListener
{
    static readonly int HeaderSize = 8 + 2 * IntPtr.Size;
    const string HidInterfaceGuid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";
    static readonly string[] KeyboardMouseInterfaceGuids =
        ["{884b96c3-56ef-11d1-bc8c-00a0c91405dd}", "{378de44c-56ef-11d1-bc8c-00a0c91405dd}"];

    /// <summary>What we subscribe to. Page-only entries cover every collection on that usage page.</summary>
    static readonly (ushort Page, ushort Usage, string Label)[] Subscriptions =
    [
        (0x01, 0x02, "Mouse"),
        (0x01, 0x06, "Keyboard"),
        (0x01, 0x04, "Joystick"),
        (0x01, 0x05, "Gamepad"),
        (0x01, 0x80, "System control"),
        (0x0B, 0x00, "Telephony"),
        (0x0C, 0x00, "Consumer / media keys"),
        (0x0D, 0x00, "Digitizers (touch, pen)"),
    ];

    readonly Dictionary<nint, DeviceInfo> _devices = [];
    byte[] _buffer = new byte[512];

    public event Action<InputEvent>? Input;
    /// <summary>A device produced a virtual-key down/up (keyboards, and media keys Windows maps to keys).</summary>
    public event Action<DeviceInfo, ushort, bool>? RawKey;
    public event Action<DeviceInfo>? DeviceArrived;
    public event Action<DeviceInfo>? DeviceRemoved;

    /// <summary>Registers for raw input on the window. Returns the labels of subscriptions Windows refused.</summary>
    public List<string> Start(HwndSource source)
    {
        var failed = new List<string>();
        foreach (var (page, usage, label) in Subscriptions)
        {
            var device = new RAWINPUTDEVICE
            {
                UsagePage = page,
                Usage = usage,
                Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY | (usage == 0 ? RIDEV_PAGEONLY : 0),
                Target = source.Handle,
            };
            if (!RegisterRawInputDevices(&device, 1, (uint)sizeof(RAWINPUTDEVICE)))
                failed.Add($"{label} ({new Win32Exception(Marshal.GetLastWin32Error()).Message})");
        }
        source.AddHook(WndProc);
        return failed;
    }

    public List<DeviceInfo> EnumerateDevices()
    {
        uint count = 0, size = (uint)sizeof(RAWINPUTDEVICELIST);
        GetRawInputDeviceList(null, ref count, size);
        var list = new RAWINPUTDEVICELIST[count];
        fixed (RAWINPUTDEVICELIST* p = list)
            count = GetRawInputDeviceList(p, ref count, size);
        return count == uint.MaxValue ? [] : list.Take((int)count).Select(d => GetDevice(d.Device)).ToList();
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_INPUT:
                OnRawInput(lParam);
                break; // not handled: DefWindowProc must still run to free the input
            case WM_INPUT_DEVICE_CHANGE when wParam == GIDC_ARRIVAL:
                DeviceArrived?.Invoke(GetDevice(lParam));
                break;
            case WM_INPUT_DEVICE_CHANGE when wParam == GIDC_REMOVAL:
                if (_devices.Remove(lParam, out var removed))
                    DeviceRemoved?.Invoke(removed);
                break;
        }
        return 0;
    }

    void OnRawInput(nint hRawInput)
    {
        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, null, ref size, (uint)HeaderSize);
        if (size == 0)
            return;
        if (_buffer.Length < size)
            _buffer = new byte[size];
        fixed (byte* p = _buffer)
            if (GetRawInputData(hRawInput, RID_INPUT, p, ref size, (uint)HeaderSize) == uint.MaxValue)
                return;

        var span = new ReadOnlySpan<byte>(_buffer, 0, (int)size);
        uint type = Read<uint>(span);
        nint hDevice = IntPtr.Size == 8 ? (nint)Read<long>(span[8..]) : Read<int>(span[8..]);
        var device = GetDevice(hDevice);
        var data = span[HeaderSize..];

        switch (type)
        {
            case RIM_TYPEKEYBOARD: OnKeyboard(device, data); break;
            case RIM_TYPEMOUSE: OnMouse(device, data); break;
            case RIM_TYPEHID: OnHid(device, data); break;
        }
    }

    // ------------------------------------------------------------------ keyboard

    void OnKeyboard(DeviceInfo d, ReadOnlySpan<byte> data)
    {
        ushort makeCode = Read<ushort>(data), flags = Read<ushort>(data[2..]), vk = Read<ushort>(data[6..]);
        if (vk == 0xFF)
            return; // fake key that is part of an escaped scan-code sequence

        bool up = (flags & RI_KEY_BREAK) != 0;
        bool repeat = false;
        if (up)
            d.PressedKeys.Remove(vk);
        else
            repeat = !d.PressedKeys.Add(vk);

        RawKey?.Invoke(d, vk, !up);

        var (name, behavior) = KeyTable.Describe(vk);
        string scan = (flags & RI_KEY_E0) != 0 ? $"E0 {makeCode:X2}" : $"{makeCode:X2}";
        Emit(d, up ? "Released" : repeat ? "Held (auto-repeat)" : "Pressed", name,
            $"VK 0x{vk:X2} · scan {scan}", behavior, $"flags=0x{flags:X}", isMinor: up || repeat,
            applied: !up && vk is not (0x10 or 0x11 or 0x12) and not (>= 0xA0 and <= 0xA5),
            trigger: up || repeat ? null : Triggers.Key(vk));
    }

    // ------------------------------------------------------------------ mouse

    static readonly (ushort Flag, string Name, bool Up, string Behavior)[] MouseButtons =
    [
        (0x0001, "Left button", false, "Primary click (select / activate); held + moved = drag"),
        (0x0002, "Left button", true, "Ends the click / drag"),
        (0x0004, "Right button", false, "Opens the context menu (on release)"),
        (0x0008, "Right button", true, "Opens the context menu"),
        (0x0010, "Middle button", false, "Opens links in a new tab; auto-scroll in browsers"),
        (0x0020, "Middle button", true, "Ends the middle click"),
        (0x0040, "X1 (Back) button", false, "Navigates back (browsers, File Explorer)"),
        (0x0080, "X1 (Back) button", true, "Ends the back click"),
        (0x0100, "X2 (Forward) button", false, "Navigates forward (browsers, File Explorer)"),
        (0x0200, "X2 (Forward) button", true, "Ends the forward click"),
    ];

    void OnMouse(DeviceInfo d, ReadOnlySpan<byte> data)
    {
        ushort flags = Read<ushort>(data), buttons = Read<ushort>(data[4..]);
        short wheel = (short)Read<ushort>(data[6..]);
        int x = Read<int>(data[12..]), y = Read<int>(data[16..]);
        bool absolute = (flags & MOUSE_MOVE_ABSOLUTE) != 0;
        string rawText = $"flags=0x{flags:X} buttons=0x{buttons:X4} x={x} y={y}";

        if (absolute)
        {
            d.AbsX = x;
            d.AbsY = y;
        }
        else if (d.Dragging)
        {
            d.DragDx += x;
            d.DragDy += y;
        }

        if (x != 0 || y != 0)
        {
            if (absolute)
                Emit(d, "Move", "Pointer position (absolute)", $"ABS {x}, {y}",
                    "Moves the cursor to an exact point — typical of touch/tablet emulation", rawText, isMovement: true);
            else
                Emit(d, "Move", "Mouse move", $"Δ {x:+0;-0;0}, {y:+0;-0;0}", "Moves the cursor", rawText,
                    isMovement: true, isRelative: true, dx: x, dy: y);
        }

        foreach (var (flag, name, up, behavior) in MouseButtons)
        {
            if ((buttons & flag) == 0)
                continue;
            string action = up ? "Released" : "Pressed";
            if (flag == 0x0001)
            {
                d.Dragging = true;
                d.DragDx = d.DragDy = 0;
                (d.DragStartAbsX, d.DragStartAbsY) = (d.AbsX, d.AbsY);
            }
            else if (flag == 0x0002 && d.Dragging)
            {
                d.Dragging = false;
                var gesture = absolute
                    ? DescribeGesture(d.AbsX - d.DragStartAbsX, d.AbsY - d.DragStartAbsY, threshold: 600, touch: true)
                    : DescribeGesture(d.DragDx, d.DragDy, threshold: 15, touch: false);
                if (!gesture.IsTap)
                {
                    Emit(d, "Drag gesture", gesture.Name, $"RI_MOUSE 0x{flag:X4}", gesture.Behavior,
                        $"{gesture.Delta} · {rawText}", applied: true, trigger: Triggers.Gesture(absolute, gesture.Id));
                    continue;
                }
                action += " — click";
            }
            Emit(d, action, name, $"RI_MOUSE 0x{flag:X4}", behavior, rawText, isMinor: up, applied: !up,
                trigger: up ? null : Triggers.Mouse(flag));
        }

        if ((buttons & 0x0400) != 0)
            Emit(d, wheel > 0 ? "Scroll up" : "Scroll down", "Mouse wheel", $"WHEEL {wheel:+0;-0}",
                $"Scrolls the content under the cursor {(wheel > 0 ? "up" : "down")} " +
                $"({Math.Abs(wheel) / 120.0:0.##} notch; 1 notch ≈ 3 lines)", rawText, applied: true,
                trigger: Triggers.Wheel(horizontal: false, positive: wheel > 0));

        if ((buttons & 0x0800) != 0)
            Emit(d, wheel > 0 ? "Scroll right" : "Scroll left", "Horizontal wheel", $"HWHEEL {wheel:+0;-0}",
                $"Scrolls the content under the cursor {(wheel > 0 ? "right" : "left")}", rawText, applied: true,
                trigger: Triggers.Wheel(horizontal: true, positive: wheel > 0));
    }

    // ------------------------------------------------------------------ generic HID

    void OnHid(DeviceInfo d, ReadOnlySpan<byte> data)
    {
        if (_directPaths.Contains(d.Path))
            return; // already read directly; don't decode it twice
        int sizeHid = (int)Read<uint>(data), count = (int)Read<uint>(data[4..]);
        for (int i = 0; i < count; i++)
        {
            int offset = 8 + i * sizeHid;
            if (offset + sizeHid > data.Length)
                break;
            OnHidReport(d, data.Slice(offset, sizeHid));
        }
    }

    void OnHidReport(DeviceInfo d, ReadOnlySpan<byte> report)
    {
        string hex = Hex(report);
        byte reportId = report.Length > 0 ? report[0] : (byte)0;

        if (d.Preparsed == 0 || report.Length == 0)
        {
            Emit(d, "Report", "Raw HID report", $"Report ID {reportId}", "Unknown — no HID descriptor available", hex);
            return;
        }

        var pressed = new HashSet<uint>();
        var values = new List<(ushort Page, ushort Usage, int Value, bool Changed, bool Absolute)>();
        bool parsed;

        fixed (byte* pr = report)
        {
            nint preparsed = d.Preparsed;
            uint length = d.MaxUsages;
            var list = stackalloc USAGE_AND_PAGE[(int)Math.Max(1, length)];
            int status = length == 0 ? HIDP_STATUS_SUCCESS
                : HidP_GetUsagesEx(HidP_Input, 0, list, ref length, preparsed, pr, (uint)report.Length);
            parsed = status == HIDP_STATUS_SUCCESS;
            if (parsed)
                for (int i = 0; i < length; i++)
                    pressed.Add((uint)list[i].UsagePage << 16 | list[i].Usage);

            foreach (var cap in d.ValueCaps)
            {
                if (cap.ReportID != reportId || cap.ReportCount != 1)
                    continue;
                for (int usage = cap.UsageMin; usage <= (cap.IsRange != 0 ? cap.UsageMax : cap.UsageMin); usage++)
                {
                    if (HidP_GetUsageValue(HidP_Input, cap.UsagePage, cap.LinkCollection, (ushort)usage,
                            out uint raw, preparsed, pr, (uint)report.Length) != HIDP_STATUS_SUCCESS)
                        continue;
                    parsed = true;
                    int value = ToSigned(raw, cap);
                    var key = (reportId, cap.UsagePage, (ushort)usage, cap.LinkCollection);
                    bool changed = !d.LastValues.TryGetValue(key, out int old) || old != value;
                    d.LastValues[key] = value;
                    values.Add((cap.UsagePage, (ushort)usage, value, changed, cap.IsAbsolute != 0));
                }
            }
        }

        if (!parsed)
        {
            Emit(d, "Report", "Unparsed HID report", $"Report ID {reportId}",
                "Not described by the device's HID descriptor — Windows ignores it", hex);
            return;
        }

        // Track the first touch contact's position for swipe detection.
        bool gotX = false, gotY = false;
        foreach (var v in values)
        {
            if (v.Page == 0x01 && v.Usage == 0x30 && !gotX) { d.TouchX = v.Value; gotX = true; }
            if (v.Page == 0x01 && v.Usage == 0x31 && !gotY) { d.TouchY = v.Value; gotY = true; }
        }

        string valueText = string.Join("  ", values.Select(v => $"{UsageTable.Describe(v.Page, v.Usage).Name}={v.Value}"));
        string details = valueText.Length > 0 ? $"{hex}  |  {valueText}" : hex;
        bool emitted = false;

        // Buttons: diff against the previous report with the same report ID.
        var previous = d.PressedByReport.GetValueOrDefault(reportId) ?? [];
        foreach (uint u in pressed.Except(previous))
        {
            var (page, usage) = ((ushort)(u >> 16), (ushort)u);
            bool isTouchDown = page == 0x0D && usage == 0x42;
            if (isTouchDown)
                (d.TouchStartX, d.TouchStartY) = (d.TouchX, d.TouchY);
            if (page == 0x0C && Triggers.ConsumerToVk.TryGetValue(usage, out ushort vk))
                RawKey?.Invoke(d, vk, true);
            // Touch-down only starts a gesture; the gesture itself is reported (and mappable) on lift-off.
            EmitUsage(d, "Pressed", page, usage, details, isMinor: false,
                applied: UsageTable.HasDefaultAction(page, usage),
                trigger: isTouchDown || page == 0x0D ? null : Triggers.Hid(page, usage));
            emitted = true;
        }
        foreach (uint u in previous.Except(pressed))
        {
            var (page, usage) = ((ushort)(u >> 16), (ushort)u);
            if (page == 0x0C && Triggers.ConsumerToVk.TryGetValue(usage, out ushort vk))
                RawKey?.Invoke(d, vk, false);
            if (page == 0x0D && usage == 0x42)
            {
                var gesture = DescribeGesture(d.TouchX - d.TouchStartX, d.TouchY - d.TouchStartY,
                    Math.Max(10, d.TouchRange / 20), touch: true);
                Emit(d, "Touch gesture", gesture.Name, "Digitizer 0x42 (finger lifted)", gesture.Behavior,
                    $"{gesture.Delta} · {details}", applied: true, trigger: Triggers.Gesture(touch: true, gesture.Id));
            }
            else
            {
                EmitUsage(d, "Released", page, usage, details, isMinor: true, applied: false, trigger: null);
            }
            emitted = true;
        }
        d.PressedByReport[reportId] = pressed;

        // Values: relative ones matter when non-zero, absolute ones when they change.
        foreach (var v in values)
        {
            if (UsageTable.IsMovement(v.Page, v.Usage) || !(v.Absolute ? v.Changed : v.Value != 0))
                continue;
            var (name, behavior) = UsageTable.Describe(v.Page, v.Usage);
            Emit(d, v.Absolute ? "Value changed" : "Value", name,
                $"{UsageTable.PageName(v.Page)} 0x{v.Usage:X2} = {v.Value}", behavior, details,
                applied: UsageTable.HasDefaultAction(v.Page, v.Usage));
            emitted = true;
        }

        if (!emitted && values.Any(v => v.Changed && UsageTable.IsMovement(v.Page, v.Usage)))
            Emit(d, "Move", $"{d.Kind} movement", valueText, "Position/axis update", hex, isMovement: true);
    }

    void EmitUsage(DeviceInfo d, string action, ushort page, ushort usage, string details, bool isMinor, bool applied,
        string? trigger)
    {
        var (name, behavior) = UsageTable.Describe(page, usage);
        Emit(d, action, name, $"{UsageTable.PageName(page)} 0x{usage:X2}", behavior, details, isMinor,
            applied: applied, trigger: trigger);
    }

    void Emit(DeviceInfo d, string action, string name, string code, string behavior, string details,
        bool isMinor = false, bool isMovement = false, bool isRelative = false, int dx = 0, int dy = 0,
        bool applied = false, string? trigger = null)
    {
        if (!isMovement)
            d.EventCount++;
        Input?.Invoke(new InputEvent(DateTime.Now, d, action, name, code, behavior, details,
            isMinor, isMovement, isRelative, dx, dy, IsApplied: applied, TriggerId: trigger));
    }

    /// <param name="Id">tap, up, down, left or right (see <see cref="Triggers.Gesture"/>).</param>
    readonly record struct Gesture(string Id, string Name, string Behavior, string Delta, bool IsTap);

    /// <param name="touch">Finger on a touch screen (or touch emulated with absolute pointer moves) vs. a mouse drag.</param>
    static Gesture DescribeGesture(int dx, int dy, int threshold, bool touch)
    {
        string delta = $"Δ {dx}, {dy}";
        if (Math.Abs(dx) < threshold && Math.Abs(dy) < threshold)
            return new("tap", touch ? "Tap" : "Click", touch
                ? "Taps the screen where the finger touched — acts like a left click there"
                : "Left click", delta, IsTap: true);

        string dir = Math.Abs(dy) >= Math.Abs(dx) ? (dy < 0 ? "up" : "down") : (dx < 0 ? "left" : "right");
        if (!touch)
            return new(dir, $"Drag {dir}", $"Left-button drag {dir}: moves or selects whatever is under the cursor", delta, false);

        string behavior = dir switch
        {
            "up" => "Finger swiped up: scrolls the content down (next item/video in feeds)",
            "down" => "Finger swiped down: scrolls the content up (previous item/video)",
            "left" => "Finger swiped left: scrolls right / next page or photo",
            _ => "Finger swiped right: scrolls left / previous page or photo",
        };
        return new(dir, $"Swipe {dir}", behavior, delta, false);
    }

    // ------------------------------------------------------------------ device info

    DeviceInfo GetDevice(nint handle)
    {
        if (handle == 0)
            return DeviceInfo.Injected;
        if (!_devices.TryGetValue(handle, out var device))
            _devices[handle] = device = CreateDevice(handle);
        return device;
    }

    static DeviceInfo CreateDevice(nint handle)
    {
        string path = GetDeviceName(handle) ?? "";

        // RID_DEVICE_INFO: cbSize, dwType, then for HID: vendor, product, version, usagePage, usage.
        var info = stackalloc byte[32];
        *(uint*)info = 32;
        uint infoSize = 32;
        GetRawInputDeviceInfo(handle, RIDI_DEVICEINFO, info, ref infoSize);
        uint type = *(uint*)(info + 4);

        string kind;
        string? vidPid = null;
        nint preparsed = 0;
        HidLayout layout = default;

        if (type == RIM_TYPEHID)
        {
            vidPid = $"{*(uint*)(info + 8):X4}:{*(uint*)(info + 12):X4}";
            kind = UsageTable.CollectionName(*(ushort*)(info + 20), *(ushort*)(info + 22));
            preparsed = GetPreparsedData(handle);
            if (preparsed != 0)
                layout = ReadLayout(preparsed);
        }
        else
        {
            kind = type == RIM_TYPEKEYBOARD ? "Keyboard" : "Mouse";
            var vid = VidRegex().Match(path);
            var pid = PidRegex().Match(path);
            if (vid.Success && pid.Success)
                vidPid = $"{vid.Groups[1].Value[^4..].ToUpperInvariant()}:{pid.Groups[1].Value.ToUpperInvariant()}";
        }

        string hidPath = ToHidInterfacePath(path);
        string upper = path.ToUpperInvariant();
        bool bluetooth = upper.Contains("00001124-0000-1000-8000-00805F9B34FB") // Bluetooth Classic HID
                         || upper.Contains("00001812-0000-1000-8000-00805F9B34FB") // Bluetooth LE HID (HOGP)
                         || upper.Contains("BTHENUM") || upper.Contains("BTHLE");
        string? product = GetHidString(hidPath, manufacturer: false)
                          ?? (vidPid is null ? null : FindSiblingProductName(handle, vidPid));

        return new DeviceInfo
        {
            Handle = handle,
            Path = path,
            Name = product
                   ?? (bluetooth ? "Bluetooth device" : upper.Contains(@"\\?\ROOT#") ? "Virtual device" : "Unnamed device")
                   + (vidPid is null ? "" : $" ({vidPid})"),
            NameIsFallback = product is null,
            Manufacturer = GetHidString(hidPath, manufacturer: true),
            Kind = kind,
            VidPid = vidPid,
            IsBluetooth = bluetooth,
            Preparsed = preparsed,
            ValueCaps = layout.ValueCaps ?? [],
            MaxUsages = layout.MaxUsages,
            TouchRange = layout.TouchRange,
        };
    }

    record struct HidLayout(ushort UsagePage, ushort Usage, uint MaxUsages, HIDP_VALUE_CAPS[]? ValueCaps, int TouchRange);

    /// <summary>What the parser needs to know about a collection's reports, from its preparsed data.</summary>
    static HidLayout ReadLayout(nint preparsed)
    {
        uint maxUsages = Math.Min(512u, HidP_MaxUsageListLength(HidP_Input, 0, preparsed));
        if (HidP_GetCaps(preparsed, out var caps) != HIDP_STATUS_SUCCESS)
            return new(0, 0, maxUsages, [], 1000);

        HIDP_VALUE_CAPS[] valueCaps = [];
        int touchRange = 1000;
        if (caps.NumberInputValueCaps > 0)
        {
            ushort n = caps.NumberInputValueCaps;
            valueCaps = new HIDP_VALUE_CAPS[n];
            fixed (HIDP_VALUE_CAPS* pc = valueCaps)
                if (HidP_GetValueCaps(HidP_Input, pc, ref n, preparsed) != HIDP_STATUS_SUCCESS)
                    n = 0;
            valueCaps = valueCaps[..n];
            var xCap = valueCaps.FirstOrDefault(c => c.UsagePage == 0x01 && c.UsageMin == 0x30);
            if (xCap.LogicalMax > 0)
                touchRange = xCap.LogicalMax - xCap.LogicalMin;
        }
        return new(caps.UsagePage, caps.Usage, maxUsages, valueCaps, touchRange);
    }

    // ------------------------------------------------------------------ devices read directly (hidden from Windows)

    readonly HashSet<string> _directPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates the entry for a collection this app reads itself (see <see cref="HiddenDeviceReader"/>) because it
    /// is hidden from Windows. Raw input for the same path, if any still arrives, is ignored from now on.
    /// </summary>
    public DeviceInfo CreateDirectDevice(string path, string name, Guid? containerId, nint preparsed)
    {
        _directPaths.Add(path);
        var layout = ReadLayout(preparsed);
        return new DeviceInfo
        {
            Path = path,
            Name = name,
            Kind = $"{UsageTable.CollectionName(layout.UsagePage, layout.Usage)} (hidden from Windows)",
            IsBluetooth = true,
            ContainerId = containerId,
            Preparsed = preparsed,
            ValueCaps = layout.ValueCaps ?? [],
            MaxUsages = layout.MaxUsages,
            TouchRange = layout.TouchRange,
        };
    }

    public void StopDirect(string path) => _directPaths.Remove(path);

    /// <summary>Feeds one input report read directly from the device through the normal decoder.</summary>
    public void ProcessDirectReport(DeviceInfo device, byte[] report) => OnHidReport(device, report);

    /// <summary>
    /// Mice/keyboards often refuse HidD_GetProductString, but another collection of the same physical
    /// device (same VID:PID) usually answers.
    /// </summary>
    static string? FindSiblingProductName(nint self, string vidPid)
    {
        uint count = 0, size = (uint)sizeof(RAWINPUTDEVICELIST);
        GetRawInputDeviceList(null, ref count, size);
        var list = new RAWINPUTDEVICELIST[count];
        fixed (RAWINPUTDEVICELIST* p = list)
            count = GetRawInputDeviceList(p, ref count, size);
        if (count == uint.MaxValue)
            return null;

        var info = stackalloc byte[32];
        foreach (var d in list.Take((int)count))
        {
            if (d.Device == self || d.Type != RIM_TYPEHID)
                continue;
            *(uint*)info = 32;
            uint infoSize = 32;
            GetRawInputDeviceInfo(d.Device, RIDI_DEVICEINFO, info, ref infoSize);
            if ($"{*(uint*)(info + 8):X4}:{*(uint*)(info + 12):X4}" != vidPid)
                continue;
            if (GetDeviceName(d.Device) is { } path && GetHidString(path, manufacturer: false) is { } name)
                return name;
        }
        return null;
    }

    static string? GetDeviceName(nint handle)
    {
        uint chars = 0;
        GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, null, ref chars);
        if (chars == 0)
            return null;
        var buffer = new char[chars];
        fixed (char* p = buffer)
            if ((int)GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, p, ref chars) <= 0)
                return null;
        return new string(buffer).TrimEnd('\0');
    }

    /// <summary>Copies the device's preparsed data to unmanaged memory kept for the device's lifetime; 0 if unavailable.</summary>
    static nint GetPreparsedData(nint handle)
    {
        uint size = 0;
        GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, null, ref size);
        if (size == 0)
            return 0;
        nint buffer = Marshal.AllocHGlobal((int)size);
        if ((int)GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, (void*)buffer, ref size) > 0)
            return buffer;
        Marshal.FreeHGlobal(buffer);
        return 0;
    }

    /// <summary>Keyboards/mice report their keyboard/mouse interface path; HidD_* needs the HID interface.</summary>
    static string ToHidInterfacePath(string path)
    {
        if (!path.StartsWith(@"\\?\HID#", StringComparison.OrdinalIgnoreCase))
            return path;
        foreach (var guid in KeyboardMouseInterfaceGuids)
        {
            int i = path.IndexOf(guid, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
                return path[..i] + HidInterfaceGuid;
        }
        return path;
    }

    static string? GetHidString(string path, bool manufacturer)
    {
        if (path.Length == 0)
            return null;
        // Zero access rights: enough for HidD_Get*String, even on devices Windows holds exclusively.
        using var handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);
        if (handle.IsInvalid)
            return null;
        var buffer = stackalloc byte[512];
        bool ok = manufacturer
            ? HidD_GetManufacturerString(handle, buffer, 512)
            : HidD_GetProductString(handle, buffer, 512);
        if (!ok)
            return null;
        string s = new((char*)buffer);
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    static int ToSigned(uint raw, in HIDP_VALUE_CAPS cap) =>
        cap.LogicalMin < 0 && cap.BitSize is > 0 and < 32 && (raw & (1u << (cap.BitSize - 1))) != 0
            ? (int)(raw | (~0u << cap.BitSize))
            : (int)raw;

    static string Hex(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (byte b in bytes)
            sb.Append(b.ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }

    static T Read<T>(ReadOnlySpan<byte> span) where T : unmanaged => MemoryMarshal.Read<T>(span);

    [GeneratedRegex("VID[_&]([0-9A-F]{4,6})", RegexOptions.IgnoreCase)]
    private static partial Regex VidRegex();

    [GeneratedRegex("PID[_&]([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex PidRegex();
}

using static InputDeviceInterceptor.Native.NativeMethods;

namespace InputDeviceInterceptor.Mapping;

/// <summary>Synthesizes scroll and keyboard input with SendInput, tagged so our own hook lets it through.</summary>
public static unsafe class InputSender
{
    /// <summary>Marks input injected by this app (KBDLLHOOKSTRUCT/MOUSEINPUT dwExtraInfo): "RING".</summary>
    public const nuint Signature = 0x52494E47;

    static readonly HashSet<ushort> ExtendedKeys =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, // Page Up/Down, End, Home, arrows
        0x2C, 0x2D, 0x2E,                               // Print Screen, Insert, Delete
        0x5B, 0x5C, 0x5D,                               // Windows keys, Menu
        0xA3, 0xA5,                                     // right Ctrl / Alt
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xAB, 0xAC,       // browser keys
        0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3,       // volume / media keys
        0xB4, 0xB5, 0xB6, 0xB7,                         // launch keys
    ];

    /// <param name="notches">Positive scrolls up, negative down. One notch = 120 wheel units ≈ 3 lines.</param>
    public static void Scroll(double notches)
    {
        var input = new INPUT { Type = INPUT_MOUSE };
        input.U.Mouse.MouseData = unchecked((uint)(int)Math.Round(notches * 120));
        input.U.Mouse.Flags = MOUSEEVENTF_WHEEL;
        input.U.Mouse.ExtraInfo = Signature;
        SendInput(1, &input, sizeof(INPUT));
    }

    /// <summary>Presses the modifiers, taps the key, then releases everything in reverse order.</summary>
    public static void KeyCombo(bool ctrl, bool alt, bool shift, bool win, ushort vk)
    {
        var modifiers = new List<ushort>();
        if (ctrl) modifiers.Add(0x11);
        if (alt) modifiers.Add(0x12);
        if (shift) modifiers.Add(0x10);
        if (win) modifiers.Add(0x5B);

        var inputs = new List<INPUT>();
        foreach (var m in modifiers)
            inputs.Add(Key(m, up: false));
        inputs.Add(Key(vk, up: false));
        inputs.Add(Key(vk, up: true));
        for (int i = modifiers.Count - 1; i >= 0; i--)
            inputs.Add(Key(modifiers[i], up: true));
        Send(inputs.ToArray());
    }

    /// <summary>Replays a key event that the hook held back.</summary>
    public static void Replay(ushort vk, ushort scan, bool up, bool extended)
    {
        var input = Key(vk, up, scan, extended);
        SendInput(1, &input, sizeof(INPUT));
    }

    static INPUT Key(ushort vk, bool up, ushort? scan = null, bool? extended = null)
    {
        var input = new INPUT { Type = INPUT_KEYBOARD };
        input.U.Keyboard.Vk = vk;
        input.U.Keyboard.Scan = scan ?? (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        input.U.Keyboard.Flags = (up ? KEYEVENTF_KEYUP : 0) | ((extended ?? ExtendedKeys.Contains(vk)) ? KEYEVENTF_EXTENDEDKEY : 0);
        input.U.Keyboard.ExtraInfo = Signature;
        return input;
    }

    static void Send(INPUT[] inputs)
    {
        fixed (INPUT* p = inputs)
            SendInput((uint)inputs.Length, p, sizeof(INPUT));
    }
}

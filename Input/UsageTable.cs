namespace InputDeviceInterceptor.Input;

/// <summary>Names and default Windows behavior for HID usages (see the USB HID Usage Tables spec).</summary>
public static class UsageTable
{
    const string AppCommand = " (sent to the focused app as WM_APPCOMMAND)";

    static readonly Dictionary<uint, (string Name, string Behavior)> Usages = new()
    {
        // Generic Desktop (0x01)
        [K(0x01, 0x30)] = ("X axis", "Horizontal position / movement"),
        [K(0x01, 0x31)] = ("Y axis", "Vertical position / movement"),
        [K(0x01, 0x32)] = ("Z axis", "Axis — used by games"),
        [K(0x01, 0x38)] = ("Wheel", "Vertical scroll"),
        [K(0x01, 0x39)] = ("Hat switch", "D-pad direction — used by games"),
        [K(0x01, 0x81)] = ("System Power Down", "Runs the power-button action (sleep / shut down, per power settings)"),
        [K(0x01, 0x82)] = ("System Sleep", "Puts the PC to sleep"),
        [K(0x01, 0x83)] = ("System Wake Up", "Wakes the PC"),
        [K(0x01, 0x90)] = ("D-pad Up", "Used by games"),
        [K(0x01, 0x91)] = ("D-pad Down", "Used by games"),
        [K(0x01, 0x92)] = ("D-pad Right", "Used by games"),
        [K(0x01, 0x93)] = ("D-pad Left", "Used by games"),

        // Keyboard page (0x07), when a keyboard shows up as a raw HID collection
        [K(0x07, 0x28)] = ("Enter", "Activates / confirms"),
        [K(0x07, 0x29)] = ("Escape", "Cancels / closes"),
        [K(0x07, 0x2C)] = ("Space", "Types a space / play-pause in video players"),
        [K(0x07, 0x4A)] = ("Home", "Goes to the start"),
        [K(0x07, 0x4B)] = ("Page Up", "Scrolls up one page"),
        [K(0x07, 0x4D)] = ("End", "Goes to the end"),
        [K(0x07, 0x4E)] = ("Page Down", "Scrolls down one page"),
        [K(0x07, 0x4F)] = ("Right arrow", "Moves right"),
        [K(0x07, 0x50)] = ("Left arrow", "Moves left"),
        [K(0x07, 0x51)] = ("Down arrow", "Moves down"),
        [K(0x07, 0x52)] = ("Up arrow", "Moves up"),
        [K(0x07, 0x7F)] = ("Mute", "Toggles system mute"),
        [K(0x07, 0x80)] = ("Volume Up", "Raises system volume"),
        [K(0x07, 0x81)] = ("Volume Down", "Lowers system volume"),

        // Telephony (0x0B)
        [K(0x0B, 0x20)] = ("Hook Switch", "Answer / hang up — used by Teams/Zoom, ignored by Windows itself"),
        [K(0x0B, 0x21)] = ("Flash", "Call flash — used by calling apps"),
        [K(0x0B, 0x24)] = ("Redial", "Used by calling apps"),
        [K(0x0B, 0x2F)] = ("Phone Mute", "Mic mute — used by Teams/Zoom"),

        // Consumer (0x0C)
        [K(0x0C, 0x30)] = ("Power", "Runs the power-button action (sleep / shut down, per power settings)"),
        [K(0x0C, 0x32)] = ("Sleep", "Puts the PC to sleep"),
        [K(0x0C, 0x40)] = ("Menu", "No default action on Windows"),
        [K(0x0C, 0x41)] = ("Menu Pick", "No default action on Windows"),
        [K(0x0C, 0x42)] = ("Menu Up", "No default action on Windows"),
        [K(0x0C, 0x43)] = ("Menu Down", "No default action on Windows"),
        [K(0x0C, 0x44)] = ("Menu Left", "No default action on Windows"),
        [K(0x0C, 0x45)] = ("Menu Right", "No default action on Windows"),
        [K(0x0C, 0x46)] = ("Menu Escape", "No default action on Windows"),
        [K(0x0C, 0x65)] = ("Camera Shutter (Snapshot)", "No default action on Windows (phones take a photo) — common on selfie remotes/rings"),
        [K(0x0C, 0x6F)] = ("Brightness Up", "Increases display brightness (laptops / supported monitors)"),
        [K(0x0C, 0x70)] = ("Brightness Down", "Decreases display brightness (laptops / supported monitors)"),
        [K(0x0C, 0xB0)] = ("Play", "Starts playback in the active media app"),
        [K(0x0C, 0xB1)] = ("Pause", "Pauses playback in the active media app"),
        [K(0x0C, 0xB2)] = ("Record", "Starts recording in apps that support it"),
        [K(0x0C, 0xB3)] = ("Fast Forward", "Fast-forwards in the active media app"),
        [K(0x0C, 0xB4)] = ("Rewind", "Rewinds in the active media app"),
        [K(0x0C, 0xB5)] = ("Next Track", "Skips to the next track in the active media app"),
        [K(0x0C, 0xB6)] = ("Previous Track", "Goes to the previous track in the active media app"),
        [K(0x0C, 0xB7)] = ("Stop", "Stops playback in the active media app"),
        [K(0x0C, 0xB8)] = ("Eject", "Ejects the optical drive"),
        [K(0x0C, 0xCD)] = ("Play/Pause", "Toggles play/pause in the active media app (Spotify, browser video, …)"),
        [K(0x0C, 0xCF)] = ("Voice Command", "Starts voice input on some Windows versions; often ignored"),
        [K(0x0C, 0xE0)] = ("Volume (relative)", "Changes system volume by the reported amount"),
        [K(0x0C, 0xE2)] = ("Mute", "Toggles system mute and shows the volume flyout"),
        [K(0x0C, 0xE9)] = ("Volume Up", "Raises system volume (2% per press) and shows the volume flyout"),
        [K(0x0C, 0xEA)] = ("Volume Down", "Lowers system volume (2% per press) and shows the volume flyout"),
        [K(0x0C, 0x183)] = ("AL Media Player", "Opens the default media player"),
        [K(0x0C, 0x18A)] = ("AL Email", "Opens the default mail app"),
        [K(0x0C, 0x192)] = ("AL Calculator", "Opens Calculator"),
        [K(0x0C, 0x194)] = ("AL File Explorer", "Opens File Explorer (This PC)"),
        [K(0x0C, 0x196)] = ("AL Browser", "Opens the default web browser"),
        [K(0x0C, 0x19E)] = ("AL Lock", "Locks the PC"),
        [K(0x0C, 0x201)] = ("AC New", "New document" + AppCommand),
        [K(0x0C, 0x202)] = ("AC Open", "Open" + AppCommand),
        [K(0x0C, 0x203)] = ("AC Close", "Close" + AppCommand),
        [K(0x0C, 0x207)] = ("AC Save", "Save" + AppCommand),
        [K(0x0C, 0x208)] = ("AC Print", "Print" + AppCommand),
        [K(0x0C, 0x21A)] = ("AC Undo", "Undo" + AppCommand),
        [K(0x0C, 0x21B)] = ("AC Copy", "Copy" + AppCommand),
        [K(0x0C, 0x21C)] = ("AC Cut", "Cut" + AppCommand),
        [K(0x0C, 0x21D)] = ("AC Paste", "Paste" + AppCommand),
        [K(0x0C, 0x221)] = ("AC Search", "Opens Windows Search"),
        [K(0x0C, 0x223)] = ("AC Home", "Opens the browser home page"),
        [K(0x0C, 0x224)] = ("AC Back", "Navigates back" + AppCommand),
        [K(0x0C, 0x225)] = ("AC Forward", "Navigates forward" + AppCommand),
        [K(0x0C, 0x226)] = ("AC Stop", "Stops loading" + AppCommand),
        [K(0x0C, 0x227)] = ("AC Refresh", "Reloads" + AppCommand),
        [K(0x0C, 0x22A)] = ("AC Bookmarks", "Opens favorites" + AppCommand),
        [K(0x0C, 0x22D)] = ("AC Zoom In", "Usually ignored by Windows"),
        [K(0x0C, 0x22E)] = ("AC Zoom Out", "Usually ignored by Windows"),
        [K(0x0C, 0x233)] = ("AC Scroll Up", "Usually ignored by Windows"),
        [K(0x0C, 0x234)] = ("AC Scroll Down", "Usually ignored by Windows"),
        [K(0x0C, 0x238)] = ("AC Pan", "Horizontal scroll (when part of a mouse); otherwise ignored"),
        [K(0x0C, 0x279)] = ("AC Redo", "Redo" + AppCommand),

        // Digitizer (0x0D)
        [K(0x0D, 0x30)] = ("Tip Pressure", "Pen pressure"),
        [K(0x0D, 0x32)] = ("In Range", "Finger/pen is near the surface"),
        [K(0x0D, 0x33)] = ("Touch", "Finger touching the surface"),
        [K(0x0D, 0x42)] = ("Tip Switch (touch)", "Finger/pen touching the surface — Windows treats it as a tap/drag on the screen"),
        [K(0x0D, 0x44)] = ("Barrel Switch", "Pen side button — right-click"),
        [K(0x0D, 0x45)] = ("Eraser", "Pen eraser"),
        [K(0x0D, 0x47)] = ("Confidence", "Contact is a real finger (not a palm)"),
        [K(0x0D, 0x48)] = ("Contact Width", "Touch contact size"),
        [K(0x0D, 0x49)] = ("Contact Height", "Touch contact size"),
        [K(0x0D, 0x51)] = ("Contact ID", "Identifies each finger in multi-touch"),
        [K(0x0D, 0x54)] = ("Contact Count", "Number of fingers in this report"),
        [K(0x0D, 0x56)] = ("Scan Time", "Timestamp"),
    };

    static uint K(ushort page, ushort usage) => (uint)page << 16 | usage;

    public static string PageName(ushort page) => page switch
    {
        0x01 => "Generic Desktop",
        0x06 => "Generic Device",
        0x07 => "Keyboard",
        0x08 => "LED",
        0x09 => "Button",
        0x0B => "Telephony",
        0x0C => "Consumer",
        0x0D => "Digitizer",
        >= 0xFF00 => "Vendor",
        _ => $"Page 0x{page:X2}",
    };

    /// <summary>Human name for a top-level collection, i.e. what kind of device it pretends to be.</summary>
    public static string CollectionName(ushort page, ushort usage) => (page, usage) switch
    {
        (0x01, 0x02) => "Mouse",
        (0x01, 0x04) => "Joystick",
        (0x01, 0x05) => "Gamepad",
        (0x01, 0x06) => "Keyboard",
        (0x01, 0x0C) => "Wireless Radio Controls (airplane mode key)",
        (0x01, 0x80) => "System Control",
        (0x0B, _) => "Telephony",
        (0x0C, 0x01) => "Consumer Control (media keys)",
        (0x0D, 0x02) => "Pen",
        (0x0D, 0x04) => "Touch Screen",
        (0x0D, 0x05) => "Touch Pad",
        _ => $"{PageName(page)} collection 0x{usage:X2}",
    };

    public static (string Name, string Behavior) Describe(ushort page, ushort usage)
    {
        if (Usages.TryGetValue(K(page, usage), out var known))
            return known;
        return page switch
        {
            0x09 => ($"Button {usage}", "Generic HID button — Windows has no default action (games/apps may read it)"),
            0x07 when usage is >= 0x04 and <= 0x1D => ($"{(char)('A' + usage - 0x04)}", $"Types '{(char)('A' + usage - 0x04)}'"),
            0x07 when usage is >= 0x1E and <= 0x27 => ($"{(usage - 0x1D) % 10}", $"Types '{(usage - 0x1D) % 10}'"),
            >= 0xFF00 => ($"Vendor 0x{usage:X2}", "Vendor-defined — only the manufacturer's app understands it; Windows ignores it"),
            _ => ($"{PageName(page)} 0x{usage:X2}", "No known default behavior on Windows"),
        };
    }

    /// <summary>Consumer usages we can name but that Windows does nothing with.</summary>
    static readonly HashSet<ushort> ConsumerWithoutAction =
        [0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x65, 0xB2, 0xCF, 0x22D, 0x22E, 0x233, 0x234, 0x238];

    /// <summary>Whether Windows itself performs an action for this usage (volume, media, navigation, typing…).</summary>
    public static bool HasDefaultAction(ushort page, ushort usage) => page switch
    {
        0x01 => usage is >= 0x81 and <= 0x83, // power / sleep / wake; axes and d-pads are for games
        0x07 => true,                        // keyboard keys always type or navigate
        0x0C => Usages.ContainsKey(K(page, usage)) && !ConsumerWithoutAction.Contains(usage),
        _ => false,                          // buttons, telephony, digitizer bookkeeping, vendor pages
    };

    /// <summary>Usages that describe position/geometry rather than a deliberate button action.</summary>
    public static bool IsMovement(ushort page, ushort usage) =>
        page == 0x0D || page == 0x01 && usage is >= 0x30 and <= 0x36;
}

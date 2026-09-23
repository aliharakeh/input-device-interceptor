using System.Windows.Input;

namespace RingInspector.Input;

/// <summary>Names and default Windows behavior for keyboard virtual-key codes.</summary>
public static class KeyTable
{
    static readonly Dictionary<int, (string Name, string Behavior)> Keys = new()
    {
        [0x08] = ("Backspace", "Deletes the character before the cursor"),
        [0x09] = ("Tab", "Moves focus to the next control"),
        [0x0D] = ("Enter", "Activates the focused button / confirms / new line"),
        [0x10] = ("Shift", "Modifier — nothing on its own"),
        [0x11] = ("Ctrl", "Modifier — nothing on its own"),
        [0x12] = ("Alt", "Modifier — alone it focuses the app's menu bar"),
        [0x13] = ("Pause", "Pauses console output; rarely used"),
        [0x14] = ("Caps Lock", "Toggles capital letters"),
        [0x1B] = ("Escape", "Cancels / closes dialogs and menus, exits full screen"),
        [0x20] = ("Space", "Types a space; clicks the focused button; play/pause in most video players; page down in browsers"),
        [0x21] = ("Page Up", "Scrolls up one page"),
        [0x22] = ("Page Down", "Scrolls down one page"),
        [0x23] = ("End", "Goes to the end of the line / page"),
        [0x24] = ("Home", "Goes to the start of the line / page"),
        [0x25] = ("Left arrow", "Moves the cursor/selection left; seeks back in many video players"),
        [0x26] = ("Up arrow", "Moves the cursor/selection up; scrolls up a little"),
        [0x27] = ("Right arrow", "Moves the cursor/selection right; seeks forward in many video players"),
        [0x28] = ("Down arrow", "Moves the cursor/selection down; scrolls down a little"),
        [0x2C] = ("Print Screen", "Opens the Snipping Tool screen capture (Windows 11 default)"),
        [0x2D] = ("Insert", "Toggles insert/overwrite mode"),
        [0x2E] = ("Delete", "Deletes the selection / next character"),
        [0x5B] = ("Left Windows", "Opens the Start menu"),
        [0x5C] = ("Right Windows", "Opens the Start menu"),
        [0x5D] = ("Menu (Apps)", "Opens the context menu"),
        [0x5F] = ("Sleep", "Puts the PC to sleep"),
        [0x90] = ("Num Lock", "Toggles the numeric keypad"),
        [0x91] = ("Scroll Lock", "Toggles scroll lock (mostly ignored)"),
        [0xA0] = ("Left Shift", "Modifier — nothing on its own"),
        [0xA1] = ("Right Shift", "Modifier — nothing on its own"),
        [0xA2] = ("Left Ctrl", "Modifier — nothing on its own"),
        [0xA3] = ("Right Ctrl", "Modifier — nothing on its own"),
        [0xA4] = ("Left Alt", "Modifier — alone it focuses the app's menu bar"),
        [0xA5] = ("Right Alt", "Modifier (AltGr on some layouts)"),
        [0xA6] = ("Browser Back", "Navigates back (browsers, File Explorer, Settings)"),
        [0xA7] = ("Browser Forward", "Navigates forward"),
        [0xA8] = ("Browser Refresh", "Reloads the page"),
        [0xA9] = ("Browser Stop", "Stops loading the page"),
        [0xAA] = ("Browser Search", "Opens Windows Search"),
        [0xAB] = ("Browser Favorites", "Opens favorites/bookmarks in the browser"),
        [0xAC] = ("Browser Home", "Opens the default browser's home page"),
        [0xAD] = ("Volume Mute", "Toggles system mute and shows the volume flyout"),
        [0xAE] = ("Volume Down", "Lowers system volume (2% per press) and shows the volume flyout"),
        [0xAF] = ("Volume Up", "Raises system volume (2% per press) and shows the volume flyout"),
        [0xB0] = ("Next Track", "Skips to the next track in the active media app"),
        [0xB1] = ("Previous Track", "Goes to the previous track in the active media app"),
        [0xB2] = ("Media Stop", "Stops playback in the active media app"),
        [0xB3] = ("Play/Pause", "Toggles play/pause in the active media app (Spotify, browser video, …)"),
        [0xB4] = ("Launch Mail", "Opens the default mail app"),
        [0xB5] = ("Launch Media", "Opens the default media player"),
        [0xB6] = ("Launch App 1", "Opens File Explorer (This PC)"),
        [0xB7] = ("Launch App 2", "Opens Calculator"),
    };

    public static (string Name, string Behavior) Describe(int vk)
    {
        if (Keys.TryGetValue(vk, out var known))
            return known;
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
            return ($"{(char)vk}", $"Types '{(char)vk}' in the focused text field (may trigger app shortcuts)");
        if (vk is >= 0x60 and <= 0x69)
            return ($"Numpad {vk - 0x60}", $"Types '{vk - 0x60}' (or navigates when Num Lock is off)");
        if (vk is >= 0x70 and <= 0x87)
            return ($"F{vk - 0x6F}", "Function key — app specific (F1 help, F5 refresh, F11 full screen…)");

        var key = KeyInterop.KeyFromVirtualKey(vk);
        return (key == Key.None ? $"VK 0x{vk:X2}" : key.ToString(), "No well-known default — depends on the focused app");
    }
}

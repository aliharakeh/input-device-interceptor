using System.Runtime.InteropServices;
using System.Windows.Threading;
using InputDeviceInterceptor.Input;
using static InputDeviceInterceptor.Native.NativeMethods;

namespace InputDeviceInterceptor.Mapping;

/// <summary>
/// Suppresses key events (including media keys Windows synthesizes from HID consumer controls) but only when
/// they come from a device that has a "block default" mapping.
/// <para>
/// A low-level keyboard hook sees every key but not which device sent it; Raw Input knows the device but can't
/// block. So the hook holds back candidate keys until the matching raw input arrives (or has already arrived):
/// keys from a blocked device are swallowed, everything else is replayed with SendInput. Raw input normally
/// arrives within a few milliseconds; if it never does, the key is replayed after <see cref="PendingTimeout"/>.
/// </para>
/// </summary>
public sealed unsafe class KeyBlocker : IDisposable
{
    static readonly TimeSpan PendingTimeout = TimeSpan.FromMilliseconds(150);
    static readonly TimeSpan RawNoteLifetime = TimeSpan.FromMilliseconds(300);
    static KeyBlocker? s_active;

    readonly Func<ushort, bool> _isCandidate;
    readonly Func<DeviceInfo, ushort, bool> _shouldBlock;
    readonly List<RawNote> _rawNotes = [];
    readonly List<PendingKey> _pending = [];
    readonly HashSet<ushort> _heldBlocked = [];
    readonly DispatcherTimer _timer;
    nint _hook;

    record struct RawNote(ushort Vk, bool Down, bool Block, long Time);
    record struct PendingKey(ushort Vk, bool Down, ushort Scan, bool Extended, long Time);

    /// <param name="isCandidate">Whether any blocking rule exists for this virtual key (hot path: keep it cheap).</param>
    /// <param name="shouldBlock">Whether this device's press of this key must be swallowed.</param>
    public KeyBlocker(Func<ushort, bool> isCandidate, Func<DeviceInfo, ushort, bool> shouldBlock)
    {
        _isCandidate = isCandidate;
        _shouldBlock = shouldBlock;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(30), DispatcherPriority.Input, (_, _) => Expire(),
            Dispatcher.CurrentDispatcher);
    }

    /// <summary>Installs the hook on the calling (UI) thread; the callback runs on this thread's message loop.</summary>
    public void Install()
    {
        s_active = this;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, &HookProc, GetModuleHandle(0), 0);
        _timer.Start();
    }

    /// <summary>Called from Raw Input with the virtual key a device produced.</summary>
    public void NoteRaw(DeviceInfo device, ushort vk, bool down)
    {
        if (!_isCandidate(vk))
            return;
        bool block = _shouldBlock(device, vk);
        int i = _pending.FindIndex(p => p.Vk == vk && p.Down == down);
        if (i >= 0)
        {
            var pending = _pending[i];
            _pending.RemoveAt(i);
            Track(vk, down, block);
            if (!block)
                InputSender.Replay(pending.Vk, pending.Scan, !pending.Down, pending.Extended);
        }
        else
        {
            _rawNotes.Add(new RawNote(vk, down, block, Environment.TickCount64));
        }
    }

    [UnmanagedCallersOnly]
    static nint HookProc(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code >= 0 && s_active is { } blocker && blocker.Swallow((KBDLLHOOKSTRUCT*)lParam))
                return 1;
        }
        catch (Exception)
        {
            // never let an exception escape into the native hook chain
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    bool Swallow(KBDLLHOOKSTRUCT* key)
    {
        ushort vk = (ushort)key->VkCode;
        if (key->ExtraInfo == InputSender.Signature || !_isCandidate(vk))
            return false;

        bool down = (key->Flags & LLKHF_UP) == 0;
        if (down && _heldBlocked.Contains(vk))
            return true; // auto-repeat of a key we're already blocking

        int i = _rawNotes.FindIndex(n => n.Vk == vk && n.Down == down);
        if (i >= 0)
        {
            var note = _rawNotes[i];
            _rawNotes.RemoveAt(i);
            Track(vk, down, note.Block);
            return note.Block;
        }

        // Raw input hasn't told us the device yet: hold the key back and decide when it does.
        _pending.Add(new PendingKey(vk, down, (ushort)key->ScanCode, (key->Flags & LLKHF_EXTENDED) != 0,
            Environment.TickCount64));
        return true;
    }

    void Track(ushort vk, bool down, bool blocked)
    {
        if (blocked && down)
            _heldBlocked.Add(vk);
        else if (!down)
            _heldBlocked.Remove(vk);
    }

    void Expire()
    {
        long now = Environment.TickCount64;
        _rawNotes.RemoveAll(n => now - n.Time > RawNoteLifetime.TotalMilliseconds);
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var pending = _pending[i];
            if (now - pending.Time <= PendingTimeout.TotalMilliseconds)
                continue;
            _pending.RemoveAt(i);
            InputSender.Replay(pending.Vk, pending.Scan, !pending.Down, pending.Extended);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_hook != 0)
            UnhookWindowsHookEx(_hook);
        _hook = 0;
        if (s_active == this)
            s_active = null;
    }
}

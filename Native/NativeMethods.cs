using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace InputDeviceInterceptor.Native;

/// <summary>Win32 Raw Input + HID parser (hid.dll) interop.</summary>
internal static unsafe partial class NativeMethods
{
    public const int WM_INPUT_DEVICE_CHANGE = 0x00FE;
    public const int WM_INPUT = 0x00FF;
    public const int GIDC_ARRIVAL = 1;
    public const int GIDC_REMOVAL = 2;

    public const uint RID_INPUT = 0x10000003;
    public const uint RIDI_PREPARSEDDATA = 0x20000005;
    public const uint RIDI_DEVICENAME = 0x20000007;
    public const uint RIDI_DEVICEINFO = 0x2000000B;

    public const uint RIDEV_PAGEONLY = 0x00000020;
    public const uint RIDEV_INPUTSINK = 0x00000100;
    public const uint RIDEV_DEVNOTIFY = 0x00002000;

    public const uint RIM_TYPEMOUSE = 0;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const uint RIM_TYPEHID = 2;

    public const ushort RI_KEY_BREAK = 0x01;
    public const ushort RI_KEY_E0 = 0x02;
    public const ushort MOUSE_MOVE_ABSOLUTE = 0x01;

    public const int HidP_Input = 0;
    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICELIST
    {
        public nint Device;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USAGE_AND_PAGE
    {
        public ushort Usage;
        public ushort UsagePage;
    }

    /// <summary>Only the fields we need; offsets match the native 72-byte struct.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 72)]
    public struct HIDP_VALUE_CAPS
    {
        [FieldOffset(0)] public ushort UsagePage;
        [FieldOffset(2)] public byte ReportID;
        [FieldOffset(6)] public ushort LinkCollection;
        [FieldOffset(12)] public byte IsRange;
        [FieldOffset(15)] public byte IsAbsolute;
        [FieldOffset(18)] public ushort BitSize;
        [FieldOffset(20)] public ushort ReportCount;
        [FieldOffset(40)] public int LogicalMin;
        [FieldOffset(44)] public int LogicalMax;
        [FieldOffset(56)] public ushort UsageMin; // == Usage when IsRange == 0
        [FieldOffset(58)] public ushort UsageMax;
    }

    // --- input injection + low-level keyboard hook ---

    public const int WH_KEYBOARD_LL = 13;
    public const uint LLKHF_EXTENDED = 0x01;
    public const uint LLKHF_UP = 0x80;

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public INPUTUNION U;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    public static partial uint MapVirtualKey(uint code, uint mapType);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static partial nint SetWindowsHookEx(int hookId, delegate* unmanaged<int, nint, nint, nint> proc,
        nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    public static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(nint moduleName);

    // --- raw input ---

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices(RAWINPUTDEVICE* devices, uint count, uint size);

    [LibraryImport("user32.dll")]
    public static partial uint GetRawInputData(nint rawInput, uint command, byte* data, ref uint size, uint headerSize);

    [LibraryImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW")]
    public static partial uint GetRawInputDeviceInfo(nint device, uint command, void* data, ref uint size);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint GetRawInputDeviceList(RAWINPUTDEVICELIST* list, ref uint count, uint size);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetCaps(nint preparsed, out HIDP_CAPS caps);

    [LibraryImport("hid.dll")]
    public static partial uint HidP_MaxUsageListLength(int reportType, ushort usagePage, nint preparsed);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetUsagesEx(int reportType, ushort linkCollection, USAGE_AND_PAGE* list,
        ref uint length, nint preparsed, byte* report, uint reportLength);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetValueCaps(int reportType, HIDP_VALUE_CAPS* caps, ref ushort length, nint preparsed);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage,
        out uint value, nint preparsed, byte* report, uint reportLength);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetPreparsedData(SafeFileHandle device, out nint preparsed);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetProductString(SafeFileHandle device, byte* buffer, uint length);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetManufacturerString(SafeFileHandle device, byte* buffer, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security,
        uint creation, uint flags, nint template);
}

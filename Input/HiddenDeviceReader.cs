using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using static InputDeviceInterceptor.Native.NativeMethods;

namespace InputDeviceInterceptor.Input;

/// <summary>
/// Reads input reports straight from a HID collection that is hidden from Windows (by HidHide), so Windows no
/// longer acts on it but this app still sees it. Reports are decoded by <see cref="RawInputListener"/> on the UI
/// thread exactly like raw input. Reconnects automatically when the device sleeps or drops off.
/// </summary>
public sealed class HiddenDeviceReader(
    string path, string name, Guid? containerId, RawInputListener listener, Dispatcher dispatcher) : IDisposable
{
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    readonly CancellationTokenSource _cts = new();

    public string Path => path;
    public string Status { get; private set; } = "Starting…";

    /// <summary>Raised on the UI thread when the device has been (re)opened.</summary>
    public event Action<DeviceInfo>? Opened;
    /// <summary>Raised on the UI thread when the device can't be read (asleep, disconnected, not allowed…).</summary>
    public event Action<string>? StatusChanged;

    public void Start() => Task.Run(() => RunAsync(_cts.Token));

    async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ReadUntilDisconnectedAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus($"Reading stopped: {ex.Message}");
            }
            try
            {
                await Task.Delay(RetryDelay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    async Task ReadUntilDisconnectedAsync(CancellationToken ct)
    {
        // Open like a regular HID client (read + write); fall back to read-only if write access is refused.
        var handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED, 0);
        if (handle.IsInvalid && Marshal.GetLastWin32Error() == 5)
        {
            handle.Dispose();
            handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED, 0);
        }
        using var ownedHandle = handle;
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            SetStatus(error switch
            {
                2 or 3 => "Waiting for the device (asleep or disconnected)…",
                5 => "Access denied — this copy of the app isn't on HidHide's allowed list. Block touch again to add it.",
                32 => "Windows still holds the touch channel — turn the ring off and on (or Allow, then Block again).",
                _ => $"Can't open the device (error {error})",
            });
            return;
        }
        if (!HidD_GetPreparsedData(handle, out nint preparsed))
        {
            SetStatus("Can't read the device's HID descriptor");
            return;
        }
        // The preparsed data stays allocated for the DeviceInfo's lifetime (a few hundred bytes per reconnect).

        HidP_GetCaps(preparsed, out var caps);
        var device = await dispatcher.InvokeAsync(() => listener.CreateDirectDevice(path, name, containerId, preparsed));
        await dispatcher.InvokeAsync(() => Opened?.Invoke(device));
        SetStatus("Reading touch directly — Windows doesn't see it");

        await using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 0, isAsync: true);
        var buffer = new byte[Math.Max((int)caps.InputReportByteLength, 1)];
        while (!ct.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                break;
            var report = buffer[..read];
            _ = dispatcher.InvokeAsync(() => listener.ProcessDirectReport(device, report));
        }
    }

    void SetStatus(string status)
    {
        Status = status;
        dispatcher.InvokeAsync(() => StatusChanged?.Invoke(status));
    }

    public void Dispose()
    {
        _cts.Cancel();
        listener.StopDirect(path);
    }
}

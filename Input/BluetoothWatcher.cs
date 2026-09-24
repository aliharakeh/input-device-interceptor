using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace RingInspector.Input;

/// <summary>A paired Bluetooth device (Classic and LE endpoints of the same device are merged).</summary>
public sealed class BluetoothDeviceItem : INotifyPropertyChanged
{
    public Guid ContainerId { get; init; }

    string _name = "", _transport = "", _inputSummary = "";
    bool _isConnected;
    int _eventCount;

    public string Name { get => _name; set => Set(ref _name, value); }
    public bool IsConnected { get => _isConnected; set { Set(ref _isConnected, value); Raise(nameof(Status)); } }
    public string Transport { get => _transport; set { Set(ref _transport, value); Raise(nameof(Status)); } }
    public string Status => $"{(IsConnected ? "Connected" : "Not connected")} · {Transport}";
    public string InputSummary { get => _inputSummary; set => Set(ref _inputSummary, value); }
    public int EventCount { get => _eventCount; set => Set(ref _eventCount, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        Raise(name);
    }

    void Raise(string name) => PropertyChanged?.Invoke(this, new(name));
}

/// <summary>Watches paired Bluetooth Classic and LE devices and their connection state.</summary>
public sealed class BluetoothWatcher(Dispatcher dispatcher)
{
    const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    const string ContainerKey = "System.Devices.Aep.ContainerId";
    static readonly string[] RequestedProperties = [IsConnectedKey, ContainerKey];

    readonly List<DeviceWatcher> _watchers = [];
    readonly Dictionary<string, (DeviceInformation Info, bool LowEnergy)> _endpoints = [];
    readonly Dictionary<Guid, BluetoothDeviceItem> _items = [];

    public event Action<BluetoothDeviceItem>? Added;
    public event Action<BluetoothDeviceItem>? Removed;
    public event Action<BluetoothDeviceItem>? Changed;

    public void Start()
    {
        Watch(BluetoothDevice.GetDeviceSelectorFromPairingState(true), lowEnergy: false);
        Watch(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), lowEnergy: true);
    }

    public void Stop()
    {
        foreach (var watcher in _watchers)
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                watcher.Stop();
    }

    void Watch(string selector, bool lowEnergy)
    {
        var watcher = DeviceInformation.CreateWatcher(selector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
        // Watcher callbacks arrive on thread-pool threads; all state lives on the UI thread.
        watcher.Added += (_, info) => dispatcher.InvokeAsync(() =>
        {
            _endpoints[info.Id] = (info, lowEnergy);
            Rebuild(ContainerOf(info));
        });
        watcher.Updated += (_, update) => dispatcher.InvokeAsync(() =>
        {
            if (!_endpoints.TryGetValue(update.Id, out var endpoint))
                return;
            endpoint.Info.Update(update);
            Rebuild(ContainerOf(endpoint.Info));
        });
        watcher.Removed += (_, update) => dispatcher.InvokeAsync(() =>
        {
            if (_endpoints.Remove(update.Id, out var endpoint))
                Rebuild(ContainerOf(endpoint.Info));
        });
        watcher.Start();
        _watchers.Add(watcher);
    }

    void Rebuild(Guid container)
    {
        if (container == Guid.Empty)
            return;

        var parts = _endpoints.Values.Where(e => ContainerOf(e.Info) == container).ToList();
        if (parts.Count == 0)
        {
            if (_items.Remove(container, out var gone))
                Removed?.Invoke(gone);
            return;
        }

        bool isNew = !_items.TryGetValue(container, out var item);
        item ??= new BluetoothDeviceItem { ContainerId = container };
        item.Name = parts.Select(p => p.Info.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? "Unnamed Bluetooth device";
        item.IsConnected = parts.Any(p => p.Info.Properties.TryGetValue(IsConnectedKey, out var c) && c is true);
        bool classic = parts.Any(p => !p.LowEnergy), le = parts.Any(p => p.LowEnergy);
        item.Transport = classic && le ? "Classic + LE" : le ? "Bluetooth LE" : "Bluetooth Classic";

        if (isNew)
        {
            _items[container] = item;
            Added?.Invoke(item);
        }
        else
        {
            Changed?.Invoke(item);
        }
    }

    static Guid ContainerOf(DeviceInformation info) =>
        info.Properties.TryGetValue(ContainerKey, out var value) && value is Guid g ? g : Guid.Empty;
}

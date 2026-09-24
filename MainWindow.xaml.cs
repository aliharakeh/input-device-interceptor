using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using RingInspector.Input;
using RingInspector.Mapping;
using Windows.Devices.Enumeration;

namespace RingInspector;

public partial class MainWindow : Window
{
    const int MaxEvents = 2000;
    const string ContainerIdKey = "System.Devices.ContainerId";
    static readonly TimeSpan MovementMergeWindow = TimeSpan.FromMilliseconds(400);

    readonly RawInputListener _listener = new();
    readonly BluetoothWatcher _bluetoothWatcher;
    readonly ObservableCollection<InputEvent> _events = [];
    readonly ObservableCollection<DeviceInfo> _devices = [];
    readonly ObservableCollection<BluetoothDeviceItem> _bluetooth = [];
    readonly ICollectionView _view;
    readonly MappingEngine _mappings = new();
    readonly KeyBlocker _keyBlocker;
    readonly ListCollectionView _rulesView;
    readonly TouchBlockState _touchBlock = TouchBlockStore.Load();
    readonly Dictionary<string, HiddenDeviceReader> _readers = new(StringComparer.OrdinalIgnoreCase);
    string? _touchMessage;
    InputEvent? _lastMappable;

    public MainWindow()
    {
        InitializeComponent();

        _view = CollectionViewSource.GetDefaultView(_events);
        _view.Filter = o => PassesFilter((InputEvent)o);
        EventGrid.ItemsSource = _view;

        _rulesView = new ListCollectionView(_mappings.Rules) { Filter = o => ((MappingRule)o).DeviceKey == SelectedDeviceKey };
        RuleList.ItemsSource = _rulesView;
        _mappings.Rules.CollectionChanged += (_, _) => UpdateNoRulesHint();
        _keyBlocker = new KeyBlocker(_mappings.IsBlockCandidate, _mappings.ShouldBlock);
        _listener.RawKey += _keyBlocker.NoteRaw;
        UpdateMappingControls();

        // One dropdown: paired Bluetooth devices first, then individual input collections.
        DevicePicker.ItemsSource = new CompositeCollection
        {
            new CollectionContainer { Collection = _bluetooth },
            new CollectionContainer { Collection = _devices },
        };

        _listener.Input += OnInput;
        _listener.DeviceArrived += AddDevice;
        _listener.DeviceRemoved += d =>
        {
            _devices.Remove(d);
            UpdateInputSummaries();
        };

        _bluetoothWatcher = new BluetoothWatcher(Dispatcher);
        _bluetoothWatcher.Added += item =>
        {
            _bluetooth.Add(item);
            UpdateInputSummary(item);
        };
        _bluetoothWatcher.Removed += item => _bluetooth.Remove(item);
        _bluetoothWatcher.Changed += UpdateInputSummary;

        SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            var failed = _listener.Start(source);
            foreach (var device in _listener.EnumerateDevices())
                AddDevice(device);
            if (failed.Count > 0)
                Status.Text = "Could not subscribe to: " + string.Join(", ", failed);
            _bluetoothWatcher.Start();
            _keyBlocker.Install();
            if (_touchBlock.ReadWhileBlocked)
                foreach (var blocked in _touchBlock.Collections)
                    StartReader(blocked);
        };
        Closed += (_, _) =>
        {
            _bluetoothWatcher.Stop();
            _keyBlocker.Dispose();
            foreach (var reader in _readers.Values)
                reader.Dispose();
        };
    }

    void OnInput(InputEvent e)
    {
        if (e.TriggerId is not null)
        {
            var fired = _mappings.Execute(e);
            if (fired.Count > 0)
            {
                string summary = string.Join(", ", fired.Select(r => r.Summary));
                bool blocked = fired.Any(r => r.BlockDefault && r.CanBlock);
                e = e with
                {
                    Action = $"{e.Action} → {summary}",
                    Behavior = blocked
                        ? $"Mapped to {summary}; Windows default blocked ({e.Behavior})"
                        : $"Mapped to {summary}; Windows default still runs: {e.Behavior}",
                    IsApplied = true,
                };
            }
            if (IsFromSelectedDevice(e))
                NoteMappable(e);
        }

        if (e.IsMovement)
        {
            if (ShowMovement.IsChecked != true)
                return;
            if (_events.Count > 0 && _events[0] is { IsMovement: true } last && last.Device == e.Device
                && last.Name == e.Name && e.Time - last.Time < MovementMergeWindow)
            {
                _events[0] = last.MergeWith(e);
                return;
            }
        }
        else if (BluetoothOwner(e.Device) is { } owner)
        {
            owner.EventCount++;
        }

        _events.Insert(0, e);
        if (_events.Count > MaxEvents)
            _events.RemoveAt(_events.Count - 1);

        if (!e.IsMovement && !e.IsMinor && (AppliedOnly.IsChecked != true || e.IsApplied) && IsFromSelectedDevice(e))
            ShowLast(e);
    }

    void ShowLast(InputEvent e)
    {
        LastName.Text = e.Name;
        LastAction.Text = $"{e.Action} at {e.TimeText}";
        LastCode.Text = e.Code;
        LastDevice.Text = BluetoothOwner(e.Device) is { } owner
            ? $"{owner.Name} (Bluetooth) · {e.Device.Kind}"
            : $"{e.Device.Display}{(e.Device.IsBluetooth ? " · Bluetooth" : "")}";
        LastBehavior.Text = e.Behavior;
        LastDetails.Text = e.Details;
        Flash.BeginAnimation(OpacityProperty, new DoubleAnimation(0.22, 0, TimeSpan.FromMilliseconds(500)));
    }

    void ResetLast(string title)
    {
        LastName.Text = title;
        LastAction.Text = LastCode.Text = LastDevice.Text = LastBehavior.Text = LastDetails.Text = "";
    }

    bool PassesFilter(InputEvent e) =>
        (ShowReleases.IsChecked == true || !e.IsMinor)
        && (AppliedOnly.IsChecked != true || e.IsApplied)
        && IsFromSelectedDevice(e);

    bool IsFromSelectedDevice(InputEvent e) => DevicePicker.SelectedItem switch
    {
        BluetoothDeviceItem bt => e.Device.ContainerId == bt.ContainerId,
        DeviceInfo device => e.Device == device,
        _ => false,
    };

    /// <summary>The paired Bluetooth device this input collection belongs to, if any.</summary>
    BluetoothDeviceItem? BluetoothOwner(DeviceInfo device) =>
        device.ContainerId is { } container ? _bluetooth.FirstOrDefault(b => b.ContainerId == container) : null;

    void AddDevice(DeviceInfo device)
    {
        // Vendor-defined collections are never subscribed to, so they would only add noise.
        if (_devices.Contains(device) || device.Kind.StartsWith("Vendor"))
            return;
        // Bluetooth devices first, then alphabetical.
        int index = 0;
        while (index < _devices.Count && Compare(_devices[index], device) <= 0)
            index++;
        _devices.Insert(index, device);
        _ = ResolveContainerAsync(device);

        static int Compare(DeviceInfo a, DeviceInfo b) =>
            a.IsBluetooth != b.IsBluetooth
                ? (a.IsBluetooth ? -1 : 1)
                : string.Compare(a.Display, b.Display, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Links an input collection to its physical device, so it can be matched to a Bluetooth pairing.</summary>
    async Task ResolveContainerAsync(DeviceInfo device)
    {
        if (device.Path.Length == 0 || device.ContainerId is not null)
            return;
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(device.Path, [ContainerIdKey], DeviceInformationKind.DeviceInterface);
            if (info.Properties.TryGetValue(ContainerIdKey, out var value) && value is Guid container)
                device.ContainerId = container;
        }
        catch (Exception)
        {
            return; // some virtual devices have no PnP interface
        }
        UpdateInputSummaries();
    }

    void UpdateInputSummaries()
    {
        foreach (var item in _bluetooth)
            UpdateInputSummary(item);
        UpdateTouchPanel();
    }

    void UpdateInputSummary(BluetoothDeviceItem item)
    {
        // BLE collections rarely report a product string; the Bluetooth name is the one people recognise.
        foreach (var device in _devices.Where(d => d.NameIsFallback && d.ContainerId == item.ContainerId))
            device.Name = item.Name;
        var kinds = _devices.Where(d => d.ContainerId == item.ContainerId).Select(d => d.Kind).Distinct().ToList();
        item.InputSummary = kinds.Count > 0 ? "· Input: " + string.Join(", ", kinds)
            : item.IsConnected ? "· No input interface"
            : "· Connect it to see its inputs";
    }

    void DevicePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool selected = DevicePicker.SelectedItem is not null;
        EmptyHint.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
        ResetLast(selected ? "Waiting for input…" : "Choose a device");
        _view.Refresh();
        _lastMappable = null;
        UpdateMappingControls();
    }

    // ------------------------------------------------------------------ mappings

    string? SelectedDeviceKey => DevicePicker.SelectedItem switch
    {
        BluetoothDeviceItem bt => MappingEngine.DeviceKey(bt),
        DeviceInfo device => MappingEngine.DeviceKey(device),
        _ => null,
    };

    string SelectedDeviceName => DevicePicker.SelectedItem switch
    {
        BluetoothDeviceItem bt => bt.Name,
        DeviceInfo device => device.Display,
        _ => "",
    };

    /// <summary>Input collections behind the selection (all of a Bluetooth device's, or just the one).</summary>
    IEnumerable<DeviceInfo> SelectedCollections => DevicePicker.SelectedItem switch
    {
        BluetoothDeviceItem bt => _devices.Where(d => d.ContainerId == bt.ContainerId),
        DeviceInfo device => [device],
        _ => [],
    };

    void NoteMappable(InputEvent e)
    {
        _lastMappable = e;
        MapLastButton.Content = $"Map last input: {Mapping.Triggers.Describe(e.TriggerId!)}";
        MapLastButton.IsEnabled = true;
    }

    void UpdateMappingControls()
    {
        bool selected = SelectedDeviceKey is not null;
        MapLastButton.Content = "Map last input";
        MapLastButton.IsEnabled = false;
        TriggerPicker.IsEnabled = selected;
        RefreshTriggerOptions();
        _rulesView.Refresh();
        UpdateNoRulesHint();
        _touchMessage = null;
        UpdateTouchPanel();
    }

    /// <summary>Triggers offered in the "add" picker: the usual ones for this kind of device plus any seen from it.</summary>
    void RefreshTriggerOptions()
    {
        var seen = _events.Where(e => e.TriggerId is not null && IsFromSelectedDevice(e)).Select(e => e.TriggerId!);
        var ids = Mapping.Triggers.Suggested(SelectedCollections.Select(d => d.Kind)).Concat(seen).Distinct();
        TriggerPicker.ItemsSource = ids.Select(id => new TriggerOption(id, Mapping.Triggers.Describe(id))).ToList();
        TriggerPicker.SelectedIndex = TriggerPicker.Items.Count > 0 ? 0 : -1;
    }

    void UpdateNoRulesHint()
    {
        NoRulesHint.Text = SelectedDeviceKey is null
            ? "Pick a device from the list above to see and edit its mappings."
            : _rulesView.IsEmpty
                ? $"No mappings for {SelectedDeviceName} yet.\nPress a button or swipe on it, then click \"Map last input\"."
                : "";
    }

    void AddRule(string triggerId)
    {
        if (SelectedDeviceKey is not { } key)
            return;
        _mappings.Rules.Add(new MappingRule
        {
            DeviceKey = key,
            DeviceName = SelectedDeviceName,
            TriggerId = triggerId,
            BlockDefault = Mapping.Triggers.CanBlock(triggerId),
        });
        _rulesView.Refresh();
        UpdateNoRulesHint();
    }

    void TriggerPicker_DropDownOpened(object? sender, EventArgs e) => RefreshTriggerOptions();

    void MapLast_Click(object sender, RoutedEventArgs e)
    {
        if (_lastMappable?.TriggerId is { } trigger)
            AddRule(trigger);
    }

    void AddMapping_Click(object sender, RoutedEventArgs e)
    {
        if (TriggerPicker.SelectedItem is TriggerOption option)
            AddRule(option.Id);
    }

    void RemoveMapping_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is MappingRule rule)
            _mappings.Rules.Remove(rule);
        UpdateNoRulesHint();
    }

    void MappingsActive_Changed(object sender, RoutedEventArgs e)
    {
        // Fires during InitializeComponent (IsChecked="True"), before the engine exists.
        if (_mappings is not null)
            _mappings.Enabled = MappingsActive.IsChecked == true;
    }

    // ------------------------------------------------------------------ touch blocking (HidHide)

    /// <summary>The Bluetooth device behind the selection (itself, or the owner of a selected collection).</summary>
    BluetoothDeviceItem? SelectedBluetooth => DevicePicker.SelectedItem switch
    {
        BluetoothDeviceItem bt => bt,
        DeviceInfo device => BluetoothOwner(device),
        _ => null,
    };

    bool IsReadDirectly(DeviceInfo device) => _readers.ContainsKey(device.Path) && device.Handle == 0;

    List<DeviceInfo> VisibleTouchCollections(BluetoothDeviceItem bt) =>
        _devices.Where(d => d.ContainerId == bt.ContainerId && d.Kind.StartsWith("Touch Screen") && !IsReadDirectly(d)).ToList();

    void UpdateTouchPanel()
    {
        var bt = SelectedBluetooth;
        var blocked = bt is null ? [] : _touchBlock.Collections.Where(c => c.DeviceKey == MappingEngine.DeviceKey(bt)).ToList();
        bool hasTouch = bt is not null && (blocked.Count > 0 || VisibleTouchCollections(bt).Count > 0);
        TouchPanel.Visibility = hasTouch ? Visibility.Visible : Visibility.Collapsed;
        if (!hasTouch)
            return;

        TouchTitle.Text = $"Touch input from {bt!.Name}";
        string status;
        if (!HidHide.IsInstalled)
        {
            status = "Blocking touch needs HidHide, a free driver that hides a device from Windows but not from this app.";
            TouchBlockButton.Content = "Get HidHide";
        }
        else if (blocked.Count > 0)
        {
            string reading = _touchBlock.ReadWhileBlocked
                ? string.Join(" ", blocked.Select(c => _readers.GetValueOrDefault(c.InterfacePath)?.Status).OfType<string>().Distinct())
                : "The app isn't reading touch, so touch mappings are paused.";
            status = $"Blocked: Windows doesn't receive this device's touch. {reading}";
            TouchBlockButton.Content = "Allow touch again";
        }
        else
        {
            status = "Windows acts on this device's touch: swipes scroll whatever is under the fake finger, on top of your mappings. " +
                     "Blocking hides the touch channel with HidHide (asks for administrator approval once).";
            TouchBlockButton.Content = "Block touch from Windows";
        }
        TouchStatus.Text = _touchMessage is null ? status : $"{_touchMessage}\n{status}";
        ReadTouchBox.Visibility = blocked.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReadTouchBox.IsChecked = _touchBlock.ReadWhileBlocked;
    }

    void ReadTouch_Changed(object sender, RoutedEventArgs e)
    {
        // Also fires when UpdateTouchPanel syncs the box, and during InitializeComponent.
        if (_touchBlock is null || ReadTouchBox.IsChecked == _touchBlock.ReadWhileBlocked)
            return;
        _touchBlock.ReadWhileBlocked = ReadTouchBox.IsChecked == true;
        TouchBlockStore.Save(_touchBlock);
        foreach (var entry in _touchBlock.Collections)
        {
            if (_touchBlock.ReadWhileBlocked)
            {
                if (!_readers.ContainsKey(entry.InterfacePath))
                    StartReader(entry);
            }
            else if (_readers.Remove(entry.InterfacePath, out var reader))
            {
                reader.Dispose();
                RemoveDevicesWithPath(entry.InterfacePath);
            }
        }
        UpdateInputSummaries();
    }

    async void TouchBlock_Click(object sender, RoutedEventArgs e)
    {
        if (!HidHide.IsInstalled)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(HidHide.DownloadUrl) { UseShellExecute = true });
            return;
        }
        if (SelectedBluetooth is not { } bt)
            return;

        string key = MappingEngine.DeviceKey(bt);
        var blocked = _touchBlock.Collections.Where(c => c.DeviceKey == key).ToList();
        TouchBlockButton.IsEnabled = false;
        _touchMessage = "Waiting for administrator approval…";
        UpdateTouchPanel();
        try
        {
            _touchMessage = blocked.Count > 0 ? await AllowTouchAsync(blocked) : await BlockTouchAsync(bt, key);
        }
        finally
        {
            TouchBlockButton.IsEnabled = true;
            UpdateTouchPanel();
        }
    }

    /// <returns>A message for the panel if something went wrong, otherwise null.</returns>
    async Task<string?> BlockTouchAsync(BluetoothDeviceItem bt, string key)
    {
        var entries = new List<BlockedCollection>();
        foreach (var device in VisibleTouchCollections(bt))
            entries.Add(new BlockedCollection(key, bt.Name, device.Path, await GetInstanceIdAsync(device.Path), bt.ContainerId));
        if (entries.Count == 0)
            return "The device's touch channel isn't connected right now — wake the ring and try again.";

        bool turnCloakOn = HidHide.IsCloakOn() != true;
        if (!await HidHide.HideAsync(entries.Select(c => c.InstanceId).ToList(), turnCloakOn))
            return "Not blocked: administrator approval was declined, or HidHide didn't accept the change.";

        _touchBlock.Collections.AddRange(entries);
        _touchBlock.CloakEnabledByApp |= turnCloakOn;
        TouchBlockStore.Save(_touchBlock);
        foreach (var entry in entries)
        {
            RemoveDevicesWithPath(entry.InterfacePath);
            if (_touchBlock.ReadWhileBlocked)
                StartReader(entry);
        }
        UpdateInputSummaries();
        return null;
    }

    async Task<string?> AllowTouchAsync(List<BlockedCollection> blocked)
    {
        bool turnCloakOff = _touchBlock.CloakEnabledByApp && _touchBlock.Collections.Count == blocked.Count;
        if (!await HidHide.UnhideAsync(blocked.Select(c => c.InstanceId).ToList(), turnCloakOff))
            return "Still blocked: administrator approval was declined, or HidHide didn't accept the change.";

        foreach (var entry in blocked)
        {
            if (_readers.Remove(entry.InterfacePath, out var reader))
                reader.Dispose();
            RemoveDevicesWithPath(entry.InterfacePath);
            _touchBlock.Collections.Remove(entry);
        }
        if (turnCloakOff)
            _touchBlock.CloakEnabledByApp = false;
        TouchBlockStore.Save(_touchBlock);
        // The restarted collection comes back through raw input (device arrival) on its own.
        foreach (var device in _listener.EnumerateDevices())
            AddDevice(device);
        UpdateInputSummaries();
        return null;
    }

    void StartReader(BlockedCollection entry)
    {
        var reader = new HiddenDeviceReader(entry.InterfacePath, entry.DeviceName, entry.ContainerId, _listener, Dispatcher);
        reader.Opened += device =>
        {
            RemoveDevicesWithPath(device.Path);
            AddDevice(device);
            UpdateInputSummaries();
        };
        reader.StatusChanged += _ => UpdateTouchPanel();
        _readers[entry.InterfacePath] = reader;
        reader.Start();
    }

    void RemoveDevicesWithPath(string path)
    {
        foreach (var stale in _devices.Where(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase)).ToList())
            _devices.Remove(stale);
    }

    /// <summary>PnP instance id (what HidHide and pnputil want) for a HID interface path.</summary>
    static async Task<string> GetInstanceIdAsync(string interfacePath)
    {
        const string instanceIdKey = "System.Devices.DeviceInstanceId";
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync(interfacePath, [instanceIdKey], DeviceInformationKind.DeviceInterface);
            if (info.Properties.TryGetValue(instanceIdKey, out var value) && value is string id && id.Length > 0)
                return id;
        }
        catch (Exception)
        {
            // fall back to deriving it from the path
        }
        // \\?\HID#<hardware>#<instance>#{interface guid}  ->  HID\<hardware>\<instance>
        var parts = interfacePath.TrimStart('\\', '?').Split('#');
        return string.Join('\\', parts[..^1]);
    }

    void Filter_Changed(object sender, RoutedEventArgs e)
    {
        // Filter checkboxes fire during InitializeComponent, before the view exists.
        _view?.Refresh();
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        _events.Clear();
        foreach (var device in _devices)
            device.EventCount = 0;
        foreach (var item in _bluetooth)
            item.EventCount = 0;
        if (DevicePicker.SelectedItem is not null)
            ResetLast("Waiting for input…");
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder("Time\tAction\tButton\tCode\tDefault behavior\tDevice\tVID:PID\tDetails\n");
        foreach (InputEvent row in _view)
            sb.AppendLine(string.Join('\t', row.TimeText, row.Action, row.Name, row.Code, row.Behavior,
                row.DeviceText, row.Device.VidPid, row.Details));
        Clipboard.SetText(sb.ToString());
    }
}

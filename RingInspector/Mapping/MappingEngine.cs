using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using RingInspector.Input;

namespace RingInspector.Mapping;

/// <summary>Holds the mapping rules, runs their operations and tells the <see cref="KeyBlocker"/> what to block.</summary>
public sealed class MappingEngine
{
    HashSet<ushort> _blockableVks = [];
    bool _enabled = true;

    public ObservableCollection<MappingRule> Rules { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            Recompute();
        }
    }

    public MappingEngine()
    {
        Rules = new ObservableCollection<MappingRule>(MappingStore.Load());
        foreach (var rule in Rules)
            rule.PropertyChanged += OnRuleChanged;
        Rules.CollectionChanged += OnRulesChanged;
        Recompute();
    }

    public static string DeviceKey(BluetoothDeviceItem device) => $"bt:{device.ContainerId}";
    public static string DeviceKey(DeviceInfo device) => $"dev:{device.Path}";

    /// <summary>Rule scopes an input collection belongs to: itself, and its Bluetooth device if it has one.</summary>
    static bool InScope(MappingRule rule, DeviceInfo device) =>
        rule.DeviceKey == DeviceKey(device)
        || device.ContainerId is { } container && rule.DeviceKey == $"bt:{container}";

    /// <summary>Runs every enabled rule matching this input and returns them.</summary>
    public List<MappingRule> Execute(InputEvent e)
    {
        if (!Enabled || e.TriggerId is null || e.Device == DeviceInfo.Injected)
            return [];
        var fired = Rules.Where(r => r.Enabled && r.TriggerId == e.TriggerId && InScope(r, e.Device)).ToList();
        foreach (var rule in fired)
        {
            switch (rule.Operation)
            {
                case MappingOperation.ScrollUp:
                    InputSender.Scroll(Math.Clamp(rule.ScrollAmount, 0.1, 100));
                    break;
                case MappingOperation.ScrollDown:
                    InputSender.Scroll(-Math.Clamp(rule.ScrollAmount, 0.1, 100));
                    break;
                case MappingOperation.Keyboard:
                    InputSender.KeyCombo(rule.Ctrl, rule.Alt, rule.Shift, rule.Win, rule.KeyVk);
                    break;
            }
        }
        return fired;
    }

    public bool IsBlockCandidate(ushort vk) => _blockableVks.Contains(vk);

    public bool ShouldBlock(DeviceInfo device, ushort vk) =>
        Enabled && Rules.Any(r => r.Enabled && r.BlockDefault && Triggers.BlockableVk(r.TriggerId) == vk && InScope(r, device));

    void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (MappingRule rule in e.NewItems ?? Array.Empty<MappingRule>())
            rule.PropertyChanged += OnRuleChanged;
        foreach (MappingRule rule in e.OldItems ?? Array.Empty<MappingRule>())
            rule.PropertyChanged -= OnRuleChanged;
        Recompute();
        MappingStore.Save(Rules);
    }

    void OnRuleChanged(object? sender, PropertyChangedEventArgs e)
    {
        Recompute();
        MappingStore.Save(Rules);
    }

    void Recompute() => _blockableVks = Enabled
        ? Rules.Where(r => r.Enabled && r.BlockDefault)
            .Select(r => Triggers.BlockableVk(r.TriggerId))
            .OfType<ushort>()
            .ToHashSet()
        : [];
}

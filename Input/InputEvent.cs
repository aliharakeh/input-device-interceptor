namespace RingInspector.Input;

/// <summary>One decoded input: a button press/release, a scroll, a key, or (optionally) movement.</summary>
/// <param name="IsMinor">Releases and key repeats — hidden unless "Show releases" is on.</param>
/// <param name="IsMovement">Pointer/touch/axis movement — only recorded when "Show movement" is on.</param>
/// <param name="IsApplied">Windows performs an action for it by default (kept by "Applied actions only").</param>
/// <param name="TriggerId">Identifies the input for mappings (see <c>Mapping.Triggers</c>); null if it can't be mapped.</param>
public sealed record InputEvent(
    DateTime Time,
    DeviceInfo Device,
    string Action,
    string Name,
    string Code,
    string Behavior,
    string Details,
    bool IsMinor = false,
    bool IsMovement = false,
    bool IsRelative = false,
    int Dx = 0,
    int Dy = 0,
    int Reports = 1,
    bool IsApplied = false,
    string? TriggerId = null)
{
    public string TimeText => Time.ToString("HH:mm:ss.fff");
    public string DeviceText => Device.Display;

    /// <summary>Collapses a stream of movement reports into a single log row.</summary>
    public InputEvent MergeWith(InputEvent next) => IsRelative && next.IsRelative
        ? next with
        {
            Dx = Dx + next.Dx,
            Dy = Dy + next.Dy,
            Reports = Reports + 1,
            Code = $"Δ {Dx + next.Dx:+0;-0;0}, {Dy + next.Dy:+0;-0;0}",
            Details = $"{Reports + 1} reports",
        }
        : next with { Reports = Reports + 1, Details = $"{next.Details} · {Reports + 1} reports" };
}

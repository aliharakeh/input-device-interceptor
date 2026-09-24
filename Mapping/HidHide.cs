using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace InputDeviceInterceptor.Mapping;

/// <summary>
/// Drives HidHide (https://github.com/nefarius/HidHide), a filter driver that hides HID devices from Windows and
/// from every app except the ones on its allowed list. Reading its state works unelevated; changing it needs admin,
/// so changes run through one elevated PowerShell (a single UAC prompt).
/// </summary>
public static class HidHide
{
    public const string DownloadUrl = "https://github.com/nefarius/HidHide/releases/latest";

    public static string? CliPath
    {
        get
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe");
            return File.Exists(path) ? path : null;
        }
    }

    public static bool IsInstalled => CliPath is not null;

    /// <summary>Whether hiding is globally active. Null if HidHide isn't installed or can't be queried.</summary>
    public static bool? IsCloakOn()
    {
        string? output = RunCli("--cloak-state");
        return output is null ? null : output.Contains("--cloak-on");
    }

    /// <summary>
    /// Hides the collections, allows this app to still open them, optionally turns hiding on, then restarts the
    /// collections so Windows lets go of the handles it already holds.
    /// </summary>
    /// <returns>False if the user declined the UAC prompt or a step failed.</returns>
    public static async Task<bool> HideAsync(IReadOnlyList<string> instanceIds, bool turnCloakOn)
    {
        var args = new StringBuilder();
        foreach (var id in instanceIds)
            args.Append($" --dev-hide {Quote(id)}");
        args.Append($" --app-reg {Quote(Environment.ProcessPath!)}");
        if (turnCloakOn)
            args.Append(" --cloak-on");
        if (!await RunElevatedAsync(args.ToString(), instanceIds))
            return false;
        var hidden = HiddenDevices();
        return instanceIds.All(id => hidden.Contains(id, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Unhides the collections (optionally turning hiding off) and restarts them so Windows picks them up again.</summary>
    public static async Task<bool> UnhideAsync(IReadOnlyList<string> instanceIds, bool turnCloakOff)
    {
        var args = new StringBuilder();
        foreach (var id in instanceIds)
            args.Append($" --dev-unhide {Quote(id)}");
        if (turnCloakOff)
            args.Append(" --cloak-off");
        if (!await RunElevatedAsync(args.ToString(), instanceIds))
            return false;
        var hidden = HiddenDevices();
        return !instanceIds.Any(id => hidden.Contains(id, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Device instance ids currently on HidHide's hidden list.</summary>
    public static List<string> HiddenDevices() =>
        (RunCli("--dev-list") ?? "")
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("--dev-hide"))
            .Select(line => line["--dev-hide".Length..].Trim().Trim('"'))
            .ToList();

    /// <returns>False if the UAC prompt was declined; callers verify the outcome by re-reading HidHide's state.</returns>
    static async Task<bool> RunElevatedAsync(string cliArgs, IReadOnlyList<string> restartIds)
    {
        if (CliPath is not { } cli)
            return false;

        // CLI exit codes aren't relied on (e.g. re-hiding an already hidden device); the state is verified after.
        var script = new StringBuilder();
        script.Append($"& {Quote(cli)}{cliArgs}\n");
        foreach (var id in restartIds)
            script.Append($"pnputil /restart-device {Quote(id)} | Out-Null\n");
        script.Append("exit 0\n");

        var start = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ToString()))}")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false; // UAC prompt declined
        }
    }

    static string? RunCli(string args)
    {
        if (CliPath is not { } cli)
            return null;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(cli, args)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
                return null;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>PowerShell single-quoted literal (safe for the '&amp;' and braces in device instance ids).</summary>
    static string Quote(string value) => $"'{value.Replace("'", "''")}'";
}

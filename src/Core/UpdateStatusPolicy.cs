using System;

namespace Firebot.Core;

internal enum UpdateStatus
{
    Disabled,
    Checking,
    Current,
    Available,
    Failed
}

internal static class UpdateStatusPolicy
{
    public static UpdateStatus Classify(string current, string reported, int exitCode, out string latest)
    {
        latest = null;
        var tag = (reported ?? string.Empty).Trim();
        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);
        if (exitCode != 0 || !Version.TryParse(current, out var installed) ||
            !Version.TryParse(tag, out var release))
            return UpdateStatus.Failed;

        if (release <= installed) return UpdateStatus.Current;
        latest = release.ToString();
        return UpdateStatus.Available;
    }

    public static string Describe(UpdateStatus status, string latest, string current)
    {
        return status switch
        {
            UpdateStatus.Disabled => "Auto Update is off. Open Releases to install updates manually.",
            UpdateStatus.Checking => "Checking GitHub for a newer Zdbrus Bot release...",
            UpdateStatus.Current => $"Zdbrus Bot {current} is up to date.",
            UpdateStatus.Available when !string.IsNullOrEmpty(latest) =>
                $"Update {latest} available. Close Firestone, wait for the updater, then reopen.",
            _ => "Update check failed. Open Releases to check manually."
        };
    }
}

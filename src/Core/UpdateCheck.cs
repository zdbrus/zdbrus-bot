using System;
using System.Diagnostics;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace Firebot.Core;

/// <summary>
///     Self-update without any network code inside the game: when the game closes, a hidden PowerShell helper waits for
///     the game process to exit, reads the latest zdbrus/zdbrus-bot release, and swaps Mods\ZdbrusBot.dll for the one in
///     the release zip. The new version loads on the next launch. The old DLL is kept as ZdbrusBot.dll.bak.
/// </summary>
internal static class UpdateCheck
{
    public const string ReleasesUrl = "https://github.com/zdbrus/zdbrus-bot/releases/latest";

    /// <summary>Kept for the F1 panel title; the helper runs outside the game, so this is never set.</summary>
    public static string Newer { get; private set; }

    private const string Script = @"
param([int]$GamePid, [string]$Dll, [string]$Current, [string]$LogFile)
$ErrorActionPreference = 'Stop'
function Log($m) { Add-Content -Path $LogFile -Value ((Get-Date -Format 's') + ' ' + $m) }
try {
    try { Wait-Process -Id $GamePid -Timeout 120 } catch { }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $h = @{ 'User-Agent' = 'ZdbrusBot/' + $Current }
    $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/zdbrus/zdbrus-bot/releases/latest' -Headers $h -TimeoutSec 20
    $tag = ([string]$rel.tag_name).TrimStart('v')
    if ([version]$tag -le [version]$Current) { Log ('Up to date (' + $Current + ').'); return }
    $asset = $rel.assets | Where-Object { $_.name -eq 'ZdbrusBot.zip' } | Select-Object -First 1
    if (-not $asset) { Log ('Release ' + $tag + ' has no ZdbrusBot.zip; skipped.'); return }
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('zdbrusbot_update_' + $tag)
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    New-Item -ItemType Directory $tmp | Out-Null
    $zip = Join-Path $tmp 'u.zip'
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -Headers $h -TimeoutSec 120 -UseBasicParsing
    Expand-Archive $zip (Join-Path $tmp 'x') -Force
    $new = Join-Path $tmp 'x\Mods\ZdbrusBot.dll'
    if (-not (Test-Path $new) -or (Get-Item $new).Length -lt 100000) { Log 'Downloaded DLL missing or too small; skipped.'; return }
    Copy-Item $Dll ($Dll + '.bak') -Force
    Copy-Item $new $Dll -Force
    Log ('Updated ' + $Current + ' -> ' + $tag + '. Takes effect on next launch.')
    Remove-Item $tmp -Recurse -Force
} catch { Log ('Update failed: ' + $_.Exception.Message) }
";

    public static void ScheduleOnExit()
    {
        try
        {
            var dll = Path.Combine(MelonEnvironment.MelonBaseDirectory, "Mods", "ZdbrusBot.dll");
            if (!File.Exists(dll))
            {
                Logger.Debug("[Update] Mods\\ZdbrusBot.dll not found; auto-update skipped.");
                return;
            }

            var userData = Path.Combine(MelonEnvironment.MelonBaseDirectory, "UserData");
            Directory.CreateDirectory(userData);
            var script = Path.Combine(userData, "ZdbrusBot_update.ps1");
            File.WriteAllText(script, Script);
            var log = Path.Combine(userData, "ZdbrusBot_update.log");

            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" " +
                $"-GamePid {Process.GetCurrentProcess().Id} -Dll \"{dll}\" -Current \"{Brand.Version}\" -LogFile \"{log}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        catch (Exception e)
        {
            Logger.Debug($"[Update] Could not start the updater: {e.Message}");
        }
    }
}

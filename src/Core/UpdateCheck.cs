using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using MelonLoader.Utils;

namespace Firebot.Core;

/// <summary>
///     Release checks and updates run in separate PowerShell processes. The installer waits for the game
///     to exit before replacing the DLL; no network code runs inside the game.
/// </summary>
internal static class UpdateCheck
{
    public const string ReleasesUrl = "https://github.com/zdbrus/zdbrus-bot/releases/latest";

    public static UpdateStatus Status { get; private set; } = UpdateStatus.Disabled;
    public static string Newer { get; private set; }

    private static Process _probe;
    private static DateTime _probeStarted;
    private static DateTime _nextCheck;
    private static DateTime _nextPoll;

    private const string ProbeScript = @"
$ErrorActionPreference = 'Stop'
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $rel = Invoke-RestMethod -Uri 'https://api.github.com/repos/zdbrus/zdbrus-bot/releases/latest' -Headers @{ 'User-Agent' = 'ZdbrusBot' } -TimeoutSec 20
    [Console]::Out.Write([string]$rel.tag_name)
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
";

    internal const string InstallerScript = @"
param([int]$GamePid, [string]$GameName, [string]$Dll, [string]$Current, [string]$LogFile)
$ErrorActionPreference = 'Stop'
function Log($m) { Add-Content -Path $LogFile -Value ((Get-Date -Format 's') + ' ' + $m) }
try {
    try { Wait-Process -Id $GamePid -ErrorAction Stop } catch {
        if (Get-Process -Id $GamePid -ErrorAction SilentlyContinue) { throw }
    }
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
    if (Get-Process -Name $GameName -ErrorAction SilentlyContinue) {
        Log 'Firestone restarted before the update finished; skipped. Close the game to retry.'
        return
    }
    Copy-Item $Dll ($Dll + '.bak') -Force
    Copy-Item $new $Dll -Force
    Log ('Updated ' + $Current + ' -> ' + $tag + '. Takes effect on next launch.')
    Remove-Item $tmp -Recurse -Force
} catch { Log ('Update failed: ' + $_.Exception.Message) }
";

    public static void Tick()
    {
        if (!BotSettings.AutoUpdate)
        {
            StopProbe();
            Status = UpdateStatus.Disabled;
            Newer = null;
            _nextCheck = DateTime.MinValue;
            return;
        }

        var now = DateTime.UtcNow;
        if (_probe == null)
        {
            if (now >= _nextCheck) StartProbe(now);
            return;
        }
        if (now < _nextPoll) return;
        _nextPoll = now.AddSeconds(1);

        try
        {
            if (!_probe.HasExited)
            {
                if (now - _probeStarted <= TimeSpan.FromSeconds(45)) return;
                StopProbe();
                FailProbe("The release check timed out.", now);
                return;
            }

            var output = _probe.StandardOutput.ReadToEnd();
            var error = _probe.StandardError.ReadToEnd().Trim();
            var result = UpdateStatusPolicy.Classify(Brand.Version, output, _probe.ExitCode, out var latest);
            _probe.Dispose();
            _probe = null;
            _nextCheck = now.AddHours(1);
            Status = result;
            Newer = latest;
            if (result == UpdateStatus.Failed)
                Logger.Warning($"[Update] Could not check the latest release: {(string.IsNullOrEmpty(error) ? "invalid release version" : error)}.");
            else if (result == UpdateStatus.Available)
                Logger.Info($"[Update] {latest} is available; close the game and wait for the updater before reopening.");
        }
        catch (Exception e)
        {
            StopProbe();
            FailProbe(e.Message, now);
        }
    }

    private static void StartProbe(DateTime now)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(ProbeScript));
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            _probe = Process.Start(psi) ?? throw new InvalidOperationException("The update check did not start.");
            _probeStarted = now;
            _nextPoll = now.AddSeconds(1);
            Status = UpdateStatus.Checking;
            Newer = null;
        }
        catch (Exception e)
        {
            FailProbe(e.Message, now);
        }
    }

    private static void FailProbe(string reason, DateTime now)
    {
        Status = UpdateStatus.Failed;
        Newer = null;
        _nextCheck = now.AddHours(1);
        Logger.Warning($"[Update] Could not check the latest release: {reason}");
    }

    public static void StopProbe()
    {
        if (_probe == null) return;
        try
        {
            if (!_probe.HasExited) _probe.Kill();
        }
        catch (Exception e)
        {
            Logger.Warning($"[Update] Could not stop the release check: {e.Message}");
        }
        finally
        {
            _probe.Dispose();
            _probe = null;
        }
    }

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
            File.WriteAllText(script, InstallerScript);
            var log = Path.Combine(userData, "ZdbrusBot_update.log");

            var game = Process.GetCurrentProcess();
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" " +
                $"-GamePid {game.Id} -GameName \"{game.ProcessName}\" -Dll \"{dll}\" -Current \"{Brand.Version}\" -LogFile \"{log}\"")
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

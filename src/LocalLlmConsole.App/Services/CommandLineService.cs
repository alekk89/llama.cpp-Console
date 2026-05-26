
namespace LocalLlmConsole.Services;

public static class CommandLineService
{
    public static void StartVisibleWindowsCommand(string executable, IEnumerable<string> args, bool elevated)
    {
        var command = string.Join(" ", new[] { PowerShellQuote(executable) }.Concat(args.Select(PowerShellQuote)));
        StartVisiblePowerShellScript($"& {command}", elevated);
    }

    public static void StartVisibleWslBashScript(string distro, string bashScript, bool elevated)
        => StartVisiblePowerShellScript(PowerShellWslBashScriptCommand(distro, bashScript), elevated);

    public static string PowerShellWslBashScriptCommand(string distro, string bashScript)
        => PowerShellWslBashScriptCommand(HostExecutableResolver.WslExe(), distro, bashScript);

    public static string PowerShellWslBashScriptCommand(string wslExe, string distro, string bashScript)
    {
        var encodedScript = Convert.ToBase64String(Encoding.UTF8.GetBytes(bashScript));
        return string.Join("; ", new[]
        {
            $"$bash=[System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encodedScript}'))",
            $"$bash | & {PowerShellQuote(wslExe)} -d {PowerShellQuote(distro)} -- bash -s"
        });
    }

    public static void StartVisiblePowerShellScript(string command, bool elevated)
    {
        var script = $"{command}; Write-Host ''; Write-Host 'Command finished. You can close this window.'";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo(HostExecutableResolver.WindowsPowerShellExe())
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
            Arguments = $"-NoExit -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}"
        };
        if (elevated) psi.Verb = "runas";
        Process.Start(psi);
    }

    public static string PowerShellQuote(string value)
        => "'" + (value ?? "").Replace("'", "''") + "'";

    public static string BashQuote(string value)
        => "'" + (value ?? "").Replace("'", "'\"'\"'") + "'";

    public static string FirstNonBlankLine(string value)
        => (value ?? "")
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "";

    public static string WslKillByEnvironmentMarkerCommand(string marker)
    {
        var modernMarkerValue = $"LLAMA_CPP_CONSOLE_BUILD_MARKER={marker}";
        var legacyMarkerValue = $"LOCAL_LLM_CONSOLE_BUILD_MARKER={marker}";
        return string.Join(" ", new[]
        {
            $"modern_marker={BashQuote(modernMarkerValue)};",
            $"legacy_marker={BashQuote(legacyMarkerValue)};",
            "for environ in /proc/[0-9]*/environ; do",
            "test -r \"$environ\" || continue;",
            "env=$(tr '\\0' '\\n' < \"$environ\" 2>/dev/null || true);",
            "case \"$env\" in *\"$modern_marker\"*|*\"$legacy_marker\"*) pid=${environ#/proc/}; pid=${pid%/environ}; kill \"$pid\" 2>/dev/null || true;; esac;",
            "done"
        });
    }
}

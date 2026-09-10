using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CamledianDrive.Services;

public sealed class RcloneMountService : IMountService
{
    private const string WebDavEndpoint = "https://admin.camledian.art/webdav/";
    private const string DriveLetter = "X:";
    private readonly StringBuilder _recentErrors = new();
    private Process? _mountProcess;

    public async Task MountAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (await IsMountedAsync(cancellationToken)) return;

        var rclone = ResolveRcloneExecutable();
        var obscuredPassword = await ObscurePasswordAsync(rclone, password, cancellationToken);
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CamledianDrive",
            "cache");
        Directory.CreateDirectory(cacheDir);

        _recentErrors.Clear();

        var psi = new ProcessStartInfo
        {
            FileName = rclone,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        psi.ArgumentList.Add("mount");
        psi.ArgumentList.Add(":webdav:");
        psi.ArgumentList.Add(DriveLetter);
        psi.ArgumentList.Add("--network-mode");
        psi.ArgumentList.Add("--volname");
        psi.ArgumentList.Add("CamledianDrive");
        psi.ArgumentList.Add("--vfs-cache-mode");
        psi.ArgumentList.Add("full");
        psi.ArgumentList.Add("--vfs-cache-max-size");
        psi.ArgumentList.Add("5G");
        psi.ArgumentList.Add("--vfs-cache-max-age");
        psi.ArgumentList.Add("24h");
        psi.ArgumentList.Add("--dir-cache-time");
        psi.ArgumentList.Add("30s");
        psi.ArgumentList.Add("--cache-dir");
        psi.ArgumentList.Add(cacheDir);
        psi.ArgumentList.Add("--log-level");
        psi.ArgumentList.Add("INFO");

        // Use backend environment variables so no WebDAV secret is written to
        // rclone.conf or exposed in the command-line arguments.
        psi.Environment["RCLONE_WEBDAV_URL"] = WebDavEndpoint;
        psi.Environment["RCLONE_WEBDAV_VENDOR"] = "other";
        psi.Environment["RCLONE_WEBDAV_USER"] = username;
        psi.Environment["RCLONE_WEBDAV_PASS"] = obscuredPassword;

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, args) => RememberError(args.Data);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Nepodařilo se spustit rclone.");
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new InvalidOperationException(
                "Rclone nebyl nalezen. Nainstaluj rclone nebo vlož rclone.exe do složky tools vedle Camledian Drive.",
                ex);
        }

        _mountProcess = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Do not keep the unobscured password alive in this service.
        password = string.Empty;

        await WaitForMountAsync(process, cancellationToken);
    }

    public Task UnmountAsync(CancellationToken cancellationToken = default)
    {
        var process = _mountProcess;
        _mountProcess = null;

        if (process is null) return Task.CompletedTask;

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        finally
        {
            process.Dispose();
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsMountedAsync(CancellationToken cancellationToken = default)
    {
        var process = _mountProcess;
        var mounted = process is { HasExited: false } && Directory.Exists(@"X:\");
        return Task.FromResult(mounted);
    }

    private async Task WaitForMountAsync(Process process, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 60; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                var details = GetRecentErrors();
                _mountProcess = null;
                process.Dispose();
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(details)
                        ? "Připojení skončilo dřív, než se disk vytvořil. Zkontroluj rclone, WinFsp a přihlašovací údaje."
                        : $"Připojení se nezdařilo: {details}");
            }

            if (Directory.Exists(@"X:\")) return;
            await Task.Delay(250, cancellationToken);
        }

        await UnmountAsync(cancellationToken);
        throw new TimeoutException("Camledian Drive se nepodařilo připojit do 15 sekund.");
    }

    private static async Task<string> ObscurePasswordAsync(
        string rclone,
        string password,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = rclone,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("obscure");
        psi.ArgumentList.Add("-");

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "Rclone nebyl nalezen. Nainstaluj rclone nebo vlož rclone.exe do složky tools vedle Camledian Drive.",
                ex);
        }

        if (process is null)
            throw new InvalidOperationException("Nepodařilo se spustit rclone obscure.");

        using (process)
        {
            await process.StandardInput.WriteLineAsync(password.AsMemory(), cancellationToken);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = (await stdoutTask).Trim();
            var error = (await stderrTask).Trim();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error)
                        ? "Rclone nedokázal připravit přihlašovací údaje."
                        : $"Rclone chyba: {error}");

            return output;
        }
    }

    private static string ResolveRcloneExecutable()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "rclone.exe");
        return File.Exists(bundled) ? bundled : "rclone.exe";
    }

    private void RememberError(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        lock (_recentErrors)
        {
            if (_recentErrors.Length > 4000)
                _recentErrors.Remove(0, Math.Min(2000, _recentErrors.Length));
            _recentErrors.AppendLine(Redact(line));
        }
    }

    private string GetRecentErrors()
    {
        lock (_recentErrors)
            return _recentErrors.ToString().Trim();
    }

    private static string Redact(string line)
    {
        // Keep diagnostics useful while avoiding accidental credential echoes.
        return line
            .Replace("Authorization:", "Authorization: [redacted]", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}

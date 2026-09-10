using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CamledianDrive.Services;

public sealed class RcloneMountService : IMountService
{
    private const string WebDavEndpoint = "https://admin.camledian.art/webdav/";
    private const string DriveLetter = "X:";
    private const string DriveRoot = @"X:\";

    private static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CamledianDrive");
    private static readonly string MountPidPath = Path.Combine(StateDirectory, "mount.pid");

    private readonly StringBuilder _recentErrors = new();
    private readonly object _processGate = new();
    private Process? _mountProcess;

    public async Task MountAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (await IsMountedAsync(cancellationToken)) return;

        if (Directory.Exists(DriveRoot))
        {
            throw new InvalidOperationException(
                "Písmeno X: už je ve Windows obsazené jiným diskem nebo mountem. Camledian Drive ho proto nemůže použít.");
        }

        var rclone = ResolveRcloneExecutable();
        var obscuredPassword = await ObscurePasswordAsync(rclone, password, cancellationToken);
        var cacheDir = Path.Combine(StateDirectory, "cache");
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
        psi.ArgumentList.Add("Camledian Drive");
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

        // No plaintext WebDAV secret is written to rclone.conf or exposed in
        // the long-running process command line.
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
                "Rclone nebyl nalezen v balíčku Camledian Drive.",
                ex);
        }

        AttachMountProcess(process, persistPid: true);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        password = string.Empty;

        try
        {
            await WaitForMountAsync(process, cancellationToken);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // The original mount error is more useful than cleanup failure.
            }

            ClearAttachedProcess(process.Id);
            process.Dispose();
            throw;
        }
    }

    public async Task UnmountAsync(CancellationToken cancellationToken = default)
    {
        var process = GetAttachedLiveProcess();

        if (process is null && Directory.Exists(DriveRoot))
        {
            TryAdoptExistingMount();
            process = GetAttachedLiveProcess();
        }

        if (process is null)
        {
            DeleteStoredPid();

            if (Directory.Exists(DriveRoot))
            {
                throw new InvalidOperationException(
                    "Disk X: existuje, ale Camledian Drive nedokázal určit jeho rclone proces. Odpojení bylo raději zablokováno, aby nebyl ukončen cizí disk.");
            }

            return;
        }

        var pid = process.Id;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Continue and verify whether WinFsp removed the mount anyway.
            }
        }
        finally
        {
            ClearAttachedProcess(pid);
            process.Dispose();
        }

        for (var i = 0; i < 30 && Directory.Exists(DriveRoot); i++)
            await Task.Delay(100, cancellationToken);

        if (Directory.Exists(DriveRoot))
            throw new InvalidOperationException("Rclone byl ukončen, ale disk X: je ve Windows stále viditelný. Zkus chvíli počkat nebo restartovat Průzkumníka.");
    }

    public Task<bool> IsMountedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(DriveRoot))
        {
            CleanupDeadProcessReference();
            return Task.FromResult(false);
        }

        if (GetAttachedLiveProcess() is not null)
            return Task.FromResult(true);

        return Task.FromResult(TryAdoptExistingMount());
    }

    private bool TryAdoptExistingMount()
    {
        if (!Directory.Exists(DriveRoot)) return false;

        var storedPid = ReadStoredPid();
        if (storedPid is int pid)
        {
            var storedProcess = TryOpenRcloneProcess(pid);
            if (storedProcess is not null)
            {
                AttachMountProcess(storedProcess, persistPid: true);
                return true;
            }

            DeleteStoredPid();
        }

        // Compatibility with the earlier prototype which did not persist PID:
        // adopt only when exactly one rclone process exists, so we never guess
        // among multiple unrelated rclone instances.
        Process[] candidates;
        try
        {
            candidates = Process.GetProcessesByName("rclone");
        }
        catch
        {
            return false;
        }

        var live = new List<Process>();
        foreach (var candidate in candidates)
        {
            if (IsLiveRcloneProcess(candidate))
                live.Add(candidate);
            else
                candidate.Dispose();
        }

        if (live.Count != 1)
        {
            foreach (var candidate in live)
                candidate.Dispose();
            return false;
        }

        AttachMountProcess(live[0], persistPid: true);
        return true;
    }

    private void AttachMountProcess(Process process, bool persistPid)
    {
        lock (_processGate)
        {
            if (_mountProcess is not null && !ReferenceEquals(_mountProcess, process))
            {
                try { _mountProcess.Dispose(); } catch { }
            }

            _mountProcess = process;
            process.EnableRaisingEvents = true;
            process.Exited -= MountProcess_Exited;
            process.Exited += MountProcess_Exited;

            if (persistPid)
                WriteStoredPid(process.Id);
        }
    }

    private void MountProcess_Exited(object? sender, EventArgs e)
    {
        if (sender is not Process process) return;

        try
        {
            ClearAttachedProcess(process.Id);
        }
        catch
        {
            DeleteStoredPid();
        }
    }

    private Process? GetAttachedLiveProcess()
    {
        lock (_processGate)
        {
            if (_mountProcess is null) return null;

            try
            {
                if (!_mountProcess.HasExited && IsLiveRcloneProcess(_mountProcess))
                    return _mountProcess;
            }
            catch
            {
                // Treat an inaccessible/exited process as stale.
            }

            _mountProcess = null;
            DeleteStoredPid();
            return null;
        }
    }

    private void CleanupDeadProcessReference()
    {
        lock (_processGate)
        {
            if (_mountProcess is null)
            {
                DeleteStoredPid();
                return;
            }

            try
            {
                if (!_mountProcess.HasExited) return;
            }
            catch
            {
                // stale
            }

            try { _mountProcess.Dispose(); } catch { }
            _mountProcess = null;
            DeleteStoredPid();
        }
    }

    private void ClearAttachedProcess(int pid)
    {
        lock (_processGate)
        {
            try
            {
                if (_mountProcess?.Id == pid)
                    _mountProcess = null;
            }
            catch
            {
                _mountProcess = null;
            }

            var storedPid = ReadStoredPid();
            if (storedPid == pid)
                DeleteStoredPid();
        }
    }

    private static Process? TryOpenRcloneProcess(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            if (IsLiveRcloneProcess(process)) return process;
            process.Dispose();
        }
        catch
        {
            // Stale PID or process is no longer accessible.
        }

        return null;
    }

    private static bool IsLiveRcloneProcess(Process process)
    {
        try
        {
            return !process.HasExited
                && string.Equals(process.ProcessName, "rclone", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void WriteStoredPid(int pid)
    {
        try
        {
            Directory.CreateDirectory(StateDirectory);
            File.WriteAllText(MountPidPath, pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch
        {
            // PID persistence improves recovery but must not prevent mounting.
        }
    }

    private static int? ReadStoredPid()
    {
        try
        {
            if (!File.Exists(MountPidPath)) return null;
            var text = File.ReadAllText(MountPidPath).Trim();
            return int.TryParse(text, out var pid) && pid > 0 ? pid : null;
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteStoredPid()
    {
        try
        {
            if (File.Exists(MountPidPath)) File.Delete(MountPidPath);
        }
        catch
        {
            // Best effort only.
        }
    }

    private async Task WaitForMountAsync(Process process, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 60; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                var details = GetRecentErrors();
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(details)
                        ? "Připojení skončilo dřív, než se disk vytvořil. Zkontroluj WinFsp a přihlašovací údaje."
                        : $"Připojení se nezdařilo: {details}");
            }

            if (Directory.Exists(DriveRoot)) return;
            await Task.Delay(250, cancellationToken);
        }

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
            throw new InvalidOperationException("Rclone nebyl nalezen v balíčku Camledian Drive.", ex);
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
        return line
            .Replace("Authorization:", "Authorization: [redacted]", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }
}

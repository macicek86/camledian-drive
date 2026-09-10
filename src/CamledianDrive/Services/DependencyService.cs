using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CamledianDrive.Services;

public static class DependencyService
{
    public static bool IsWinFspInstalled()
    {
        // WinFsp documents this registry location for x64/ARM64 Windows.
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\WinFsp"))
        {
            if (key is not null) return true;
        }

        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinFsp"))
        {
            if (key is not null) return true;
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        return File.Exists(Path.Combine(programFilesX86, "WinFsp", "bin", "winfsp-x64.dll"))
            || File.Exists(Path.Combine(programFiles, "WinFsp", "bin", "winfsp-x64.dll"));
    }

    public static string GetBundledWinFspInstallerPath() =>
        Path.Combine(AppContext.BaseDirectory, "dependencies", "winfsp.msi");

    public static async Task InstallBundledWinFspAsync(CancellationToken cancellationToken = default)
    {
        var installer = GetBundledWinFspInstallerPath();
        if (!File.Exists(installer))
        {
            throw new InvalidOperationException(
                "Instalátor WinFsp nebyl v balíčku nalezen. Stáhni znovu celý Camledian Drive build.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "msiexec.exe",
            Arguments = $"/i \"{installer}\"",
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Nepodařilo se spustit instalátor WinFsp.");

            await process.WaitForExitAsync(cancellationToken);

            // 0 = success, 3010 = success/restart recommended, 1641 = success/restart initiated.
            if (process.ExitCode is not (0 or 3010 or 1641))
            {
                throw new InvalidOperationException(
                    $"Instalace WinFsp skončila s kódem {process.ExitCode}.");
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Instalace WinFsp byla zrušena uživatelem.", ex);
        }
    }
}

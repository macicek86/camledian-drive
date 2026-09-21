using System.Text.Json;

namespace CamledianDrive.Services;

public sealed record TransferStatus(bool Known, int Pending, int Errors, bool OutOfSpace, string[] FailedPaths)
{
    public static TransferStatus Unknown { get; } = new(false, 0, 0, false, []);
    public bool CanDisconnect => Known && Pending == 0 && Errors == 0 && !OutOfSpace;
    public bool NeedsAttention => !Known || Errors > 0 || OutOfSpace;
    public string NotificationKey => !Known ? "unknown" : OutOfSpace ? "space" : string.Join("|", FailedPaths.Order()) + $":{Errors}";

    public string Message
    {
        get
        {
            if (!Known) return "Stav nahrávání nelze ověřit. Soubory na disku nemusí být odeslané na server.";
            if (OutOfSpace) return "V počítači dochází místo pro pracovní soubory. Uvolněte místo; nahrávání není dokončené.";
            if (Errors > 0)
            {
                var names = string.Join(", ", FailedPaths.Take(3));
                var detail = names.Length > 0 ? $" ({names})" : "";
                var hint = FailedPaths.Any(IsOrderRootFile)
                    ? " Soubor vedle zakazka.txt přesuňte do podsložky Interní."
                    : " Zkontrolujte připojení a oprávnění cílové složky. Nahrávání se automaticky opakuje.";
                return $"Nepodařilo se nahrát soubory{detail}. Zůstávají uložené jen v tomto počítači." + hint;
            }
            return Pending > 0 ? $"Probíhá nahrávání. Zbývající soubory: {Pending}. Disk zatím neodpojujte."
                : "Fronta nahrávání je prázdná.";
        }
    }

    private static bool IsOrderRootFile(string path)
    {
        var parts = path.Replace('\\', '/').Trim('/').Split('/');
        return parts.Length == 3 && (parts[0].Equals("Zakazky", StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("Zakázky", StringComparison.OrdinalIgnoreCase)
            || parts[0].Equals("orders", StringComparison.OrdinalIgnoreCase));
    }

    public static TransferStatus Parse(JsonElement stats, JsonElement queue)
    {
        // Fail closed if a future/incompatible rclone omits required fields.
        var cache = stats.GetProperty("diskCache");
        var queued = cache.GetProperty("uploadsQueued").GetInt32();
        var uploading = cache.GetProperty("uploadsInProgress").GetInt32();
        var errors = cache.GetProperty("erroredFiles").GetInt32();
        var outOfSpace = cache.GetProperty("outOfSpace").GetBoolean();
        var items = queue.GetProperty("queue");
        var failed = new List<string>();
        var count = 0;
        if (items.ValueKind != JsonValueKind.Null)
        {
            foreach (var item in items.EnumerateArray())
            {
                count++;
                var tries = item.GetProperty("tries").GetInt32();
                var active = item.GetProperty("uploading").GetBoolean();
                // Attempt #1 in progress is not a failure. A queued retry is.
                if (tries > 1 || (tries > 0 && !active))
                    failed.Add(item.GetProperty("name").GetString() ?? "soubor");
            }
        }
        return new(true, Math.Max(count, queued + uploading), Math.Max(errors, failed.Count), outOfSpace, failed.ToArray());
    }
}

public sealed class PendingTransfersException(string message) : InvalidOperationException(message);

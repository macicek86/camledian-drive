using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CamledianDrive.Services;

// RC is loopback-only and authenticated; no secrets in command-line arguments.
internal sealed record RcloneControlSession(int Port, string Password)
{
    private const string CredentialTarget = "CamledianDrive:MountControl";
    private sealed record Identity(int Pid, long Started, int Port);
    private static readonly HttpClient Client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public static RcloneControlSession Create()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new(port, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    public void Configure(ProcessStartInfo start)
    {
        start.ArgumentList.Add("--rc");
        start.ArgumentList.Add("--rc-addr");
        start.ArgumentList.Add($"127.0.0.1:{Port}");
        start.Environment["RCLONE_RC_USER"] = "camledian-drive";
        start.Environment["RCLONE_RC_PASS"] = Password;
    }

    public void Save(Process process) => CredentialService.Save(
        JsonSerializer.Serialize(new Identity(process.Id, process.StartTime.ToUniversalTime().Ticks, Port)),
        Password, CredentialTarget);

    public static RcloneControlSession? Load(Process process)
    {
        try
        {
            var saved = CredentialService.Load(CredentialTarget);
            if (saved is null) return null;
            var identity = JsonSerializer.Deserialize<Identity>(saved.Username);
            return identity is not null && identity.Pid == process.Id
                && identity.Started == process.StartTime.ToUniversalTime().Ticks
                && identity.Port is > 0 and <= 65535
                ? new(identity.Port, saved.Password) : null;
        }
        catch { return null; }
    }

    public async Task<TransferStatus> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Read queue before counters: a just-finished item only delays disconnect;
            // a newly queued item can still be caught by the counters.
            using var queue = await PostAsync("vfs/queue", cancellationToken);
            using var stats = await PostAsync("vfs/stats", cancellationToken);
            return TransferStatus.Parse(stats.RootElement, queue.RootElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return TransferStatus.Unknown; }
    }

    private async Task<JsonDocument> PostAsync(string method, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{Port}/{method}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"camledian-drive:{Password}")));
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }
}

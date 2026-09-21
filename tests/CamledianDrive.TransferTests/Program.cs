using System.Net;
using System.Text;
using System.Text.Json;
using CamledianDrive.Services;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
TransferStatus Parse(string queue, int queued = 0, int uploading = 0, int errors = 0, bool space = false)
{
    using var stats = JsonDocument.Parse(JsonSerializer.Serialize(new {
        diskCache = new { uploadsQueued = queued, uploadsInProgress = uploading, erroredFiles = errors, outOfSpace = space }
    }));
    using var items = JsonDocument.Parse(queue);
    return TransferStatus.Parse(stats.RootElement, items.RootElement);
}
var empty = Parse("{\"queue\":[]}");
Check(empty.CanDisconnect && !empty.NeedsAttention, "Empty queue permits disconnect");
Check(Parse("{\"queue\":null}").CanDisconnect, "rclone's null empty queue is supported");
Check(!TransferStatus.Unknown.CanDisconnect, "Unknown state must not permit silent disconnect");
var waiting = Parse("""{"queue":[{"name":"Interní/a.txt","tries":0,"uploading":false}]}""");
Check(waiting.Pending == 1 && waiting.Errors == 0 && !waiting.CanDisconnect, "Delayed write is pending, not completed or failed");
var active = Parse("""{"queue":[{"name":"Interní/a.txt","tries":1,"uploading":true}]}""", uploading:1);
Check(active.Errors == 0 && active.Pending == 1, "First active attempt is not a failure or double counted");
var failed = Parse("""{"queue":[{"name":"Zakazky/ABCDEF-123456/návrh.pdf","tries":1,"uploading":false}]}""", queued:1);
Check(failed.Errors == 1 && !failed.CanDisconnect && failed.NeedsAttention, "First rejected write is visible and blocks silent disconnect");
Check(failed.Message.Contains("Interní") && failed.Message.Contains("návrh.pdf"), "Rejected order-root upload has actionable hint");
var retry = Parse("""{"queue":[{"name":"Zakazky/ABCDEF-123456/návrh.pdf","tries":2,"uploading":true}]}""", uploading:1);
Check(retry.NotificationKey == failed.NotificationKey, "Retry does not repeat the same toast");
var ordinary = Parse("""{"queue":[{"name":"Zakazky/ABCDEF-123456/Interní/a.pdf","tries":1,"uploading":false}]}""");
Check(!ordinary.Message.Contains("přesuňte"), "Do not suggest moving a file already inside Interní");
Check(!Parse("{\"queue\":[]}", errors:1).CanDisconnect, "Cache error blocks even without queue entry");
Check(!Parse("{\"queue\":[]}", space:true).CanDisconnect, "Disk full blocks disconnect");
Check(!Parse("{\"queue\":[]}", queued:1).CanDisconnect, "New write after queue snapshot caught by counters");
Check(empty.CanDisconnect && empty.NotificationKey != failed.NotificationKey, "Completed upload clears failure");

// Exercise the real HTTP reader against a controlled loopback server, including
// missing fields and 403. No credentials are written and no Windows mount needed.
var session = RcloneControlSession.Create();
using var listener = new HttpListener();
listener.Prefixes.Add($"http://127.0.0.1:{session.Port}/");
listener.Start();
async Task Serve(string path, string body, int status = 200)
{
    var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Check(context.Request.HttpMethod == "POST", "RC must use POST");
    Check(context.Request.Url!.AbsolutePath == path, "Unexpected RC endpoint");
    var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"camledian-drive:{session.Password}"));
    Check(context.Request.Headers["Authorization"] == expected, "RC requires per-mount authentication");
    context.Response.StatusCode = status;
    var bytes = Encoding.UTF8.GetBytes(body);
    context.Response.ContentLength64 = bytes.Length;
    await context.Response.OutputStream.WriteAsync(bytes);
    context.Response.Close();
}
var request = session.ReadAsync(default);
await Serve("/vfs/queue", "{\"queue\":[]}");
await Serve("/vfs/stats", """{"diskCache":{"uploadsQueued":0,"uploadsInProgress":0,"erroredFiles":0,"outOfSpace":false}}""");
Check((await request).CanDisconnect, "Real HTTP success is parsed");
request = session.ReadAsync(default);
await Serve("/vfs/queue", "{}", 403);
Check(!(await request).Known, "HTTP failure must not look like an empty queue");
request = session.ReadAsync(default);
await Serve("/vfs/queue", "{\"queue\":[]}");
await Serve("/vfs/stats", "{}");
Check(!(await request).Known, "Incompatible stats fail closed");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try { await session.ReadAsync(cancelled.Token); throw new Exception("Cancellation swallowed"); }
catch (OperationCanceledException) { checks++; }
Console.WriteLine($"Passed {checks} transfer checks.");

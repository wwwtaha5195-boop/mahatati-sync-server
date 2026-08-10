using System.Text.Json;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 10 * 1024 * 1024);
WebApplication app = builder.Build();

string apiKey = Environment.GetEnvironmentVariable("MAHATATI_SYNC_API_KEY") ?? "";
if (apiKey.Length < 16) throw new InvalidOperationException("Set MAHATATI_SYNC_API_KEY to a secret of at least 16 characters.");
string dataRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("MAHATATI_SYNC_DATA") ?? Path.Combine(app.Environment.ContentRootPath, "App_Data"));
Directory.CreateDirectory(dataRoot);
Directory.CreateDirectory(Path.Combine(dataRoot, "results"));

// Render terminates public TLS and forwards traffic to this container over its
// private HTTP network. The public endpoint is still HTTPS-only at the edge.
app.Use(async (context, next) =>
{
    // Hosting platforms must be able to check service health without knowing
    // the private synchronization key. No business data is exposed here.
    if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase))
    {
        await next();
        return;
    }
    if (!context.Request.Headers.TryGetValue("X-Mahatati-Key", out var supplied) ||
        !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(supplied.ToString()), System.Text.Encoding.UTF8.GetBytes(apiKey)))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

app.MapPost("/api/sync/master", async (HttpRequest request) =>
{
    string json = await ReadAndValidateAsync(request, "mahatati-assignments");
    await AtomicWriteAsync(Path.Combine(dataRoot, "master.json"), json);
    return Results.Ok(new { stored = true });
});

app.MapGet("/api/sync/master", async () =>
{
    string path = Path.Combine(dataRoot, "master.json");
    return !File.Exists(path) ? Results.NotFound() : Results.Text(await File.ReadAllTextAsync(path), "application/json");
});

app.MapPost("/api/sync/results", async (HttpRequest request) =>
{
    string json = await ReadAndValidateAsync(request, "mahatati-results");
    using JsonDocument document = JsonDocument.Parse(json);
    string envelopeId = document.RootElement.GetProperty("EnvelopeId").GetString() ?? "";
    if (!Guid.TryParseExact(envelopeId, "N", out _)) return Results.BadRequest(new { error = "EnvelopeId must be a GUID in N format." });
    string path = Path.Combine(dataRoot, "results", envelopeId + ".json");
    bool duplicate = File.Exists(path);
    if (!duplicate) await AtomicWriteAsync(path, json);
    return Results.Ok(new { stored = true, duplicate });
});

app.MapGet("/api/sync/results", async () =>
{
    string[] files = Directory.GetFiles(Path.Combine(dataRoot, "results"), "*.json");
    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
    List<string> items = new();
    foreach (string file in files) items.Add(await File.ReadAllTextAsync(file));
    return Results.Json(new { Items = items });
});

app.MapDelete("/api/sync/results/{envelopeId}", (string envelopeId) =>
{
    if (!Guid.TryParseExact(envelopeId, "N", out _))
        return Results.BadRequest(new { error = "Invalid envelope id." });
    string path = Path.Combine(dataRoot, "results", envelopeId + ".json");
    if (!File.Exists(path)) return Results.NotFound();
    File.Delete(path);
    return Results.Ok(new { acknowledged = true });
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));
app.Run();

static async Task<string> ReadAndValidateAsync(HttpRequest request, string expectedType)
{
    using StreamReader reader = new(request.Body);
    string json = await reader.ReadToEndAsync();
    using JsonDocument document = JsonDocument.Parse(json);
    JsonElement root = document.RootElement;
    if (!root.TryGetProperty("Type", out JsonElement type) || type.GetString() != expectedType)
        throw new BadHttpRequestException("Unexpected envelope type.");
    if (!root.TryGetProperty("Version", out JsonElement version) || version.GetInt32() < 2)
        throw new BadHttpRequestException("Version 2 or later is required.");
    return json;
}

static async Task AtomicWriteAsync(string path, string content)
{
    string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    await File.WriteAllTextAsync(temp, content);
    File.Move(temp, path, true);
}

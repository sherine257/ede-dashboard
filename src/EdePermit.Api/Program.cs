using System.Security.Cryptography;
using System.Text;
using EdePermit.Api;
using EdePermit.Core;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:5088");
builder.Services.AddHttpClient("openai", c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var excel = FirstExisting(
        Path.Combine(env.ContentRootPath, "data", "ALL_REG_PACKS.xlsx"),
        Path.Combine(env.ContentRootPath, "..", "..", "data", "ALL_REG_PACKS.xlsx"),
        "/workspace/data/ALL_REG_PACKS.xlsx");
    return new MedicineRegistry(excel);
});
builder.Services.AddSingleton(sp => new BusinessRulesEngine(sp.GetRequiredService<MedicineRegistry>()));
builder.Services.AddSingleton(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    var db = Path.Combine(env.ContentRootPath, "data", "ede-permit.db");
    return new LiteStore(db);
});
builder.Services.AddSingleton<OpenAiExtractor>();
builder.Services.AddSingleton<ChatOrchestrator>();

var app = builder.Build();
var officerPassword = app.Configuration["Officer:Password"] ?? "EdeOfficer!2026";
var officerCookie = "ede_officer";

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (MedicineRegistry registry) => Results.Ok(new
{
    ok = true,
    register = registry.Count,
    source = Path.GetFileName(registry.SourcePath)
}));

app.MapPost("/api/chat/start", (ChatOrchestrator chat) => Results.Ok(chat.Start()));
app.MapPost("/api/chat/{id}/signin", (string id, ChatOrchestrator chat) => Results.Ok(chat.SignIn(id)));
app.MapGet("/api/chat/{id}", (string id, ChatOrchestrator chat) => Results.Ok(chat.Get(id)));
app.MapPost("/api/chat/{id}/message", async (string id, ChatMessageDto body, ChatOrchestrator chat, CancellationToken ct) =>
    Results.Ok(await chat.UserMessage(id, body.Text ?? "", body.InputMode ?? "text", ct)));
app.MapPost("/api/chat/{id}/upload", async (string id, HttpRequest req, ChatOrchestrator chat, CancellationToken ct) =>
{
    var form = await req.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file is null) return Results.BadRequest(new { error = "file required" });
    var type = form["documentType"].ToString();
    return Results.Ok(await chat.Upload(id, type, file, ct));
});
app.MapPost("/api/chat/{id}/transcribe", async (string id, HttpRequest req, OpenAiExtractor ai, ChatOrchestrator chat, CancellationToken ct) =>
{
    var form = await req.ReadFormAsync(ct);
    var file = form.Files.GetFile("file");
    if (file is null) return Results.BadRequest(new { error = "audio required" });
    await using var stream = file.OpenReadStream();
    var text = await ai.TranscribeAsync(stream, file.FileName, file.ContentType, ct);
    if (string.IsNullOrWhiteSpace(text))
        return Results.Ok(new { text = "", hint = "Whisper is unavailable. Use the browser microphone (voice-to-text) instead." });
    return Results.Ok(await chat.UserMessage(id, text, "voice", ct));
});

app.MapPost("/api/officer/login", (OfficerLoginDto body, HttpResponse res) =>
{
    if (!FixedEquals(body.Password ?? "", officerPassword))
        return Results.Json(new { error = "Invalid officer password" }, statusCode: 401);
    res.Cookies.Append(officerCookie, Sign(officerPassword), new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = "/",
        Expires = DateTimeOffset.UtcNow.AddHours(8)
    });
    return Results.Ok(new { ok = true });
});
app.MapPost("/api/officer/logout", (HttpResponse res) =>
{
    res.Cookies.Delete(officerCookie);
    return Results.Ok(new { ok = true });
});
app.MapGet("/api/officer/session", (HttpRequest req) =>
    OfficerOk(req) ? Results.Ok(new { ok = true }) : Results.Unauthorized());
app.MapGet("/api/officer/requests", (HttpRequest req, ChatOrchestrator chat) =>
    OfficerOk(req) ? Results.Ok(chat.OfficerQueue()) : Results.Unauthorized());
app.MapGet("/api/officer/requests/{id}", (string id, HttpRequest req, ChatOrchestrator chat) =>
{
    if (!OfficerOk(req)) return Results.Unauthorized();
    var data = chat.OfficerCase(id);
    return data is null ? Results.NotFound() : Results.Ok(data);
});
app.MapPost("/api/officer/requests/{id}/decision", (string id, OfficerDecisionDto body, HttpRequest req, ChatOrchestrator chat) =>
    OfficerOk(req) ? Results.Ok(chat.OfficerDecide(id, body.Decision ?? "", body.Notes ?? "", "officer")) : Results.Unauthorized());
app.MapGet("/api/officer/analytics", (HttpRequest req, ChatOrchestrator chat) =>
    OfficerOk(req) ? Results.Ok(chat.Analytics()) : Results.Unauthorized());
app.MapGet("/api/officer/documents/{id}", (string id, HttpRequest req, ChatOrchestrator chat) =>
{
    if (!OfficerOk(req)) return Results.Unauthorized();
    var doc = chat.FindDocument(id);
    if (doc is null || !File.Exists(doc.StoragePath)) return Results.NotFound();
    return Results.File(doc.StoragePath, string.IsNullOrWhiteSpace(doc.ContentType) ? "application/octet-stream" : doc.ContentType, doc.FileName);
});

app.MapGet("/officer", () => Results.Redirect("/officer.html"));

var registry = app.Services.GetRequiredService<MedicineRegistry>();
app.Logger.LogInformation("EDE register loaded {Count} packs from {Path}", registry.Count, registry.SourcePath);
app.Run();

bool OfficerOk(HttpRequest req) =>
    req.Cookies.TryGetValue(officerCookie, out var v) && FixedEquals(v, Sign(officerPassword));

static string Sign(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ede-officer|" + value)));

static bool FixedEquals(string a, string b) =>
    CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a.PadRight(128)), Encoding.UTF8.GetBytes(b.PadRight(128)));

static string FirstExisting(params string[] paths) =>
    paths.FirstOrDefault(File.Exists) ?? paths[0];

public sealed record ChatMessageDto(string? Text, string? InputMode);
public sealed record OfficerLoginDto(string? Password);
public sealed record OfficerDecisionDto(string? Decision, string? Notes);

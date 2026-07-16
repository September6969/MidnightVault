using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.Json.Serialization;
using MidnightVault.Core.Storage;
using MidnightVault.Receiver;
using MidnightVault.Receiver.Contracts;
using MidnightVault.Receiver.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddOptions<ReceiverOptions>()
    .BindConfiguration(ReceiverOptions.SectionName)
    .Validate(options => Path.IsPathFullyQualified(options.RootPath), "Receiver:RootPath 必须是绝对路径。")
    .Validate(
        options => options.ChunkSizeBytes is >= 1024 * 1024 and <= 64 * 1024 * 1024,
        "Receiver:ChunkSizeBytes 必须介于 1MB 与 64MB 之间。")
    .Validate(options => options.MinimumFreeSpaceBytes >= 0, "Receiver:MinimumFreeSpaceBytes 不能为负数。")
    .Validate(
        options => options.MinimumFreeSpaceRatio is >= 0 and <= 0.5,
        "Receiver:MinimumFreeSpaceRatio 必须介于 0 与 0.5 之间。")
    .Validate(options => options.CompletedSessionRetentionDays >= 1, "已完成会话至少保留 1 天。")
    .Validate(options => options.IncompleteSessionRetentionDays >= 1, "未完成会话至少保留 1 天。")
    .ValidateOnStart();
builder.Services.AddWindowsService(options => options.ServiceName = "MidnightVault Receiver");
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 65L * 1024 * 1024);
builder.Services.AddSingleton<BackupPathPolicy>();
builder.Services.AddSingleton<UploadSessionStore>();
builder.Services.AddSingleton<UploadTokenValidator>();
builder.Services.AddSingleton<StorageVolumeService>();
builder.Services.AddSingleton<CatalogStore>();
builder.Services.AddSingleton<DailyManifestWriter>();
builder.Services.AddSingleton<PairingSessionStore>();
builder.Services.AddSingleton<DeviceSignatureValidator>();
builder.Services.AddSingleton<PowerKeepAwakeService>();
builder.Services.AddSingleton<FileExtensionContentTypeProvider>();
builder.Services.AddHostedService<StagingCleanupService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<PowerKeepAwakeService>());
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => StatusCodes.Status404NotFound,
        ArgumentException or InvalidDataException => StatusCodes.Status400BadRequest,
        UnauthorizedAccessException => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError
    }
});

app.MapGet("/healthz", async (
    HttpContext context,
    StorageVolumeService storage,
    CatalogStore catalog,
    CancellationToken cancellationToken) =>
{
    try
    {
        var volume = await storage.GetSnapshotAsync(cancellationToken);
        var summary = await catalog.GetSummaryAsync(cancellationToken);
        var status = volume.AvailableFreeSpace >= volume.MinimumFreeSpaceBytes ? "ok" : "low-space";
        if (!IsLoopback(context))
        {
            return Results.Ok(new
            {
                status,
                service = "MidnightVault.Receiver",
                utc = DateTimeOffset.UtcNow
            });
        }
        return Results.Ok(new
        {
            status,
            service = "MidnightVault.Receiver",
            utc = DateTimeOffset.UtcNow,
            storage = volume,
            library = summary
        });
    }
    catch (Exception exception) when (exception is IOException or InvalidDataException)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "备份硬盘不可用",
            detail: exception.Message);
    }
});
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api/v1/uploads"),
    branch => branch.UseMiddleware<DeviceSignatureMiddleware>());

var uploads = app.MapGroup("/api/v1/uploads");

uploads.MapPost("/", async (
    StartUploadRequest request,
    HttpContext context,
    UploadTokenValidator tokenValidator,
    UploadSessionStore store,
    PowerKeepAwakeService power,
    CancellationToken cancellationToken) =>
{
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }
    if (!string.Equals(
        request.File.DeviceId,
        SignedDeviceId(context),
        StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }

    power.NotifyUploadActivity();
    var response = await store.StartAsync(request.File, cancellationToken);
    return Results.Created($"/api/v1/uploads/{response.UploadId}", response);
});

uploads.MapGet("/{uploadId}", async (
    string uploadId,
    HttpContext context,
    UploadTokenValidator tokenValidator,
    UploadSessionStore store,
    CancellationToken cancellationToken) =>
{
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }
    if (!await store.IsOwnedByDeviceAsync(
        uploadId,
        SignedDeviceId(context),
        cancellationToken))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await store.GetStatusAsync(uploadId, cancellationToken));
});

uploads.MapPut("/{uploadId}/chunks/{index:int}", async (
    string uploadId,
    int index,
    HttpContext context,
    UploadTokenValidator tokenValidator,
    UploadSessionStore store,
    PowerKeepAwakeService power,
    CancellationToken cancellationToken) =>
{
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }
    if (!await store.IsOwnedByDeviceAsync(
        uploadId,
        SignedDeviceId(context),
        cancellationToken))
    {
        return Results.Unauthorized();
    }

    if (!context.Request.Headers.TryGetValue("X-Chunk-Sha256", out var chunkHash))
    {
        return Results.BadRequest(new { error = "缺少 X-Chunk-Sha256 请求头。" });
    }

    power.NotifyUploadActivity();
    await store.PutChunkAsync(uploadId, index, context.Request.Body, chunkHash.ToString(), cancellationToken);
    return Results.NoContent();
});

uploads.MapPost("/{uploadId}/complete", async (
    string uploadId,
    HttpContext context,
    UploadTokenValidator tokenValidator,
    UploadSessionStore store,
    PowerKeepAwakeService power,
    CancellationToken cancellationToken) =>
{
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }
    if (!await store.IsOwnedByDeviceAsync(
        uploadId,
        SignedDeviceId(context),
        cancellationToken))
    {
        return Results.Unauthorized();
    }

    power.NotifyUploadActivity();
    return Results.Ok(await store.CompleteAsync(uploadId, cancellationToken));
});

var library = app.MapGroup("/api/v1/library");

library.MapGet("/", async (
    string? query,
    MidnightVault.Core.Models.BackupCategory? category,
    string? deviceId,
    int? offset,
    int? limit,
    HttpContext context,
    UploadTokenValidator tokenValidator,
    CatalogStore catalog,
    CancellationToken cancellationToken) =>
{
    if (!IsLoopback(context)) return Results.NotFound();
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }
    return Results.Ok(await catalog.QueryAsync(
        query,
        category,
        deviceId,
        offset ?? 0,
        limit ?? 100,
        cancellationToken));
});

library.MapGet("/summary", async (
    HttpContext context,
    UploadTokenValidator tokenValidator,
    CatalogStore catalog,
    CancellationToken cancellationToken) =>
{
    if (!IsLoopback(context)) return Results.NotFound();
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(await catalog.GetSummaryAsync(cancellationToken));
});

library.MapGet("/{id:long}/content", async (
    long id,
    HttpContext context,
    UploadTokenValidator tokenValidator,
    CatalogStore catalog,
    FileExtensionContentTypeProvider contentTypes,
    CancellationToken cancellationToken) =>
{
    if (!IsLoopback(context)) return Results.NotFound();
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }

    var item = await catalog.GetAsync(id, cancellationToken);
    if (item is null) return Results.NotFound();
    var path = catalog.ResolveStoredPath(item);
    if (!File.Exists(path)) return Results.NotFound();
    if (!contentTypes.TryGetContentType(path, out var contentType))
    {
        contentType = "application/octet-stream";
    }
    return Results.File(path, contentType, enableRangeProcessing: true);
});

var pairing = app.MapGroup("/api/v1/pairing");

pairing.MapPost("/sessions", (
    HttpContext context,
    UploadTokenValidator tokenValidator,
    PairingSessionStore pairingSessions) =>
{
    if (!IsLoopback(context)) return Results.NotFound();
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }
    return Results.Ok(pairingSessions.Create());
});

pairing.MapPost("/claim", async (
    ClaimPairingRequest request,
    PairingSessionStore pairingSessions,
    CancellationToken cancellationToken) =>
{
    var response = await pairingSessions.ClaimAsync(request, cancellationToken);
    return response is null ? Results.Unauthorized() : Results.Ok(response);
});

app.MapPost("/api/v1/devices/register", async (
    RegisterUploadDeviceRequest request,
    HttpContext context,
    UploadTokenValidator tokenValidator,
    CatalogStore catalog,
    CancellationToken cancellationToken) =>
{
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }
    if (string.IsNullOrWhiteSpace(request.DeviceId) ||
        string.IsNullOrWhiteSpace(request.DeviceName) ||
        string.IsNullOrWhiteSpace(request.PublicKey))
    {
        return Results.BadRequest();
    }
    await catalog.RegisterDeviceAsync(
        request.DeviceId.Trim(),
        request.DeviceName.Trim(),
        request.PublicKey.Trim(),
        cancellationToken);
    return Results.NoContent();
});

app.MapGet("/api/v1/devices", async (
    HttpContext context,
    UploadTokenValidator tokenValidator,
    CatalogStore catalog,
    CancellationToken cancellationToken) =>
{
    if (!IsLoopback(context)) return Results.NotFound();
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }
    return Results.Ok(await catalog.GetDevicesAsync(cancellationToken));
});

app.MapDelete("/api/v1/devices/{deviceId}", async (
    string deviceId,
    HttpContext context,
    UploadTokenValidator tokenValidator,
    CatalogStore catalog,
    CancellationToken cancellationToken) =>
{
    if (!IsLoopback(context)) return Results.NotFound();
    if (!tokenValidator.IsAuthorized(context)) return Results.Unauthorized();
    await catalog.RevokeDeviceAsync(deviceId, cancellationToken);
    return Results.NoContent();
});

app.Run();

static bool IsLoopback(HttpContext context)
{
    var address = context.Connection.RemoteIpAddress;
    return address is null || System.Net.IPAddress.IsLoopback(address);
}

static string SignedDeviceId(HttpContext context) =>
    context.Request.Headers["X-Device-Id"].ToString();

public partial class Program;

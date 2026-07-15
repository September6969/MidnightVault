using System.Text.Json.Serialization;
using MidnightVault.Core.Storage;
using MidnightVault.Receiver;
using MidnightVault.Receiver.Contracts;
using MidnightVault.Receiver.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ReceiverOptions>(builder.Configuration.GetSection(ReceiverOptions.SectionName));
builder.Services.AddSingleton<BackupPathPolicy>();
builder.Services.AddSingleton<UploadSessionStore>();
builder.Services.AddSingleton<UploadTokenValidator>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.UseExceptionHandler();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    service = "MidnightVault.Receiver",
    utc = DateTimeOffset.UtcNow
}));

var uploads = app.MapGroup("/api/v1/uploads");

uploads.MapPost("/", async (
    StartUploadRequest request,
    HttpContext context,
    UploadTokenValidator tokenValidator,
    UploadSessionStore store,
    CancellationToken cancellationToken) =>
{
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }

    var response = await store.StartAsync(request.File, cancellationToken);
    return Results.Created($"/api/v1/uploads/{response.UploadId}", response);
});

uploads.MapPut("/{uploadId}/chunks/{index:int}", async (
    string uploadId,
    int index,
    HttpContext context,
    UploadTokenValidator tokenValidator,
    UploadSessionStore store,
    CancellationToken cancellationToken) =>
{
    if (!tokenValidator.IsAuthorized(context))
    {
        return Results.Unauthorized();
    }

    if (!context.Request.Headers.TryGetValue("X-Chunk-Sha256", out var chunkHash))
    {
        return Results.BadRequest(new { error = "缺少 X-Chunk-Sha256 请求头。" });
    }

    await store.PutChunkAsync(uploadId, index, context.Request.Body, chunkHash.ToString(), cancellationToken);
    return Results.NoContent();
});

uploads.MapPost("/{uploadId}/complete", async (
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

    return Results.Ok(await store.CompleteAsync(uploadId, cancellationToken));
});

app.Run();

public partial class Program;

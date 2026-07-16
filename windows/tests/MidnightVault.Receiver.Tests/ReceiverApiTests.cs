using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using MidnightVault.Receiver.Contracts;

namespace MidnightVault.Receiver.Tests;

public sealed class ReceiverApiTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"midnight-vault-api-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program>? _factory;

    [Fact]
    public async Task UploadApi_认证上传状态索引与内容读取形成闭环()
    {
        using var client = CreateClient();
        var unauthorized = await client.PostAsJsonAsync("/api/v1/uploads", CreateRequest("00", 1));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        client.DefaultRequestHeaders.Add("X-MidnightVault-Token", "integration-token");
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = Convert.ToBase64String(deviceKey.ExportSubjectPublicKeyInfo());
        var registration = await client.PostAsJsonAsync("/api/v1/devices/register", new
        {
            deviceId = "android-integration",
            deviceName = "集成测试手机",
            publicKey
        });
        registration.EnsureSuccessStatusCode();
        var content = Encoding.UTF8.GetBytes("Android 到 Windows 集成测试");
        var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var startBody = JsonSerializer.SerializeToUtf8Bytes(CreateRequest(sha256, content.Length), JsonOptions);
        using var startRequest = CreateSignedRequest(
            HttpMethod.Post,
            "/api/v1/uploads",
            startBody,
            "android-integration",
            deviceKey);
        startRequest.Content!.Headers.ContentType = new("application/json");
        var startResponse = await client.SendAsync(startRequest);
        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);
        var session = await startResponse.Content.ReadFromJsonAsync<StartUploadResponse>(JsonOptions);
        Assert.NotNull(session);
        Assert.Equal(1, session.ChunkCount);

        using var chunkRequest = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/v1/uploads/{session.UploadId}/chunks/0");
        ApplySignature(chunkRequest, content, "android-integration", deviceKey);
        chunkRequest.Content = new ByteArrayContent(content);
        chunkRequest.Content.Headers.ContentType = new("application/octet-stream");
        chunkRequest.Headers.Add(
            "X-Chunk-Sha256",
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        var chunkResponse = await client.SendAsync(chunkRequest);
        Assert.Equal(HttpStatusCode.NoContent, chunkResponse.StatusCode);

        using var statusRequest = CreateSignedRequest(
            HttpMethod.Get,
            $"/api/v1/uploads/{session.UploadId}",
            [],
            "android-integration",
            deviceKey);
        var statusResponse = await client.SendAsync(statusRequest);
        statusResponse.EnsureSuccessStatusCode();
        var pendingStatus = await statusResponse.Content.ReadFromJsonAsync<UploadStatusResponse>(JsonOptions);
        Assert.NotNull(pendingStatus);
        Assert.False(pendingStatus.Completed);
        Assert.Equal([0], pendingStatus.ReceivedChunks);

        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var otherRegistration = await client.PostAsJsonAsync("/api/v1/devices/register", new
        {
            deviceId = "android-other",
            deviceName = "另一台手机",
            publicKey = Convert.ToBase64String(otherKey.ExportSubjectPublicKeyInfo())
        });
        otherRegistration.EnsureSuccessStatusCode();
        using var crossDeviceRequest = CreateSignedRequest(
            HttpMethod.Get,
            $"/api/v1/uploads/{session.UploadId}",
            [],
            "android-other",
            otherKey);
        var crossDeviceResponse = await client.SendAsync(crossDeviceRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, crossDeviceResponse.StatusCode);

        using var completeRequest = CreateSignedRequest(
            HttpMethod.Post,
            $"/api/v1/uploads/{session.UploadId}/complete",
            [],
            "android-integration",
            deviceKey);
        var completeResponse = await client.SendAsync(completeRequest);
        completeResponse.EnsureSuccessStatusCode();
        var completed = await completeResponse.Content.ReadFromJsonAsync<CompleteUploadResponse>(JsonOptions);
        Assert.NotNull(completed);

        using var repeatedRequest = CreateSignedRequest(
            HttpMethod.Post,
            $"/api/v1/uploads/{session.UploadId}/complete",
            [],
            "android-integration",
            deviceKey);
        var repeatedComplete = await client.SendAsync(repeatedRequest);
        repeatedComplete.EnsureSuccessStatusCode();

        var page = await client.GetFromJsonAsync<LibraryPageResponse>(
            "/api/v1/library?query=integration.txt&category=Document", JsonOptions);
        Assert.NotNull(page);
        var item = Assert.Single(page.Items);
        Assert.Equal(sha256, item.Sha256);

        var downloaded = await client.GetByteArrayAsync($"/api/v1/library/{item.Id}/content");
        Assert.Equal(content, downloaded);

        var summary = await client.GetFromJsonAsync<LibrarySummaryResponse>(
            "/api/v1/library/summary", JsonOptions);
        Assert.NotNull(summary);
        Assert.Equal(1, summary.FileCount);
        Assert.Equal(content.Length, summary.TotalBytes);
    }

    [Fact]
    public async Task Healthz_返回硬盘与资料库状态()
    {
        using var client = CreateClient();
        var response = await client.GetAsync("/healthz");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("volumeId", body, StringComparison.Ordinal);
        Assert.Contains("library", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PairingApi_一次性码只能领取一次并登记设备()
    {
        using var authorized = CreateClient();
        authorized.DefaultRequestHeaders.Add("X-MidnightVault-Token", "integration-token");
        var sessionResponse = await authorized.PostAsync("/api/v1/pairing/sessions", content: null);
        sessionResponse.EnsureSuccessStatusCode();
        var session = await sessionResponse.Content.ReadFromJsonAsync<CreatePairingSessionResponse>(JsonOptions);
        Assert.NotNull(session);

        using var phone = CreateClient();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new ClaimPairingRequest(
            session.Code,
            "pairing-device",
            "扫码手机",
            Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        var firstClaim = await phone.PostAsJsonAsync("/api/v1/pairing/claim", request, JsonOptions);
        firstClaim.EnsureSuccessStatusCode();
        var credential = await firstClaim.Content.ReadFromJsonAsync<ClaimPairingResponse>(JsonOptions);
        Assert.Equal("integration-token", credential?.UploadToken);

        var repeatedClaim = await phone.PostAsJsonAsync("/api/v1/pairing/claim", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, repeatedClaim.StatusCode);

        var devices = await authorized.GetFromJsonAsync<IReadOnlyList<PairedDeviceResponse>>(
            "/api/v1/devices",
            JsonOptions);
        var device = Assert.Single(devices!);
        Assert.Equal("pairing-device", device.DeviceId);

        var revoke = await authorized.DeleteAsync("/api/v1/devices/pairing-device");
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        var afterRevoke = await authorized.GetFromJsonAsync<IReadOnlyList<PairedDeviceResponse>>(
            "/api/v1/devices",
            JsonOptions);
        Assert.Empty(afterRevoke!);

        var startBody = JsonSerializer.SerializeToUtf8Bytes(CreateRequest(new string('a', 64), 1), JsonOptions);
        using var signedAfterRevoke = CreateSignedRequest(
            HttpMethod.Post,
            "/api/v1/uploads",
            startBody,
            "pairing-device",
            key);
        signedAfterRevoke.Content!.Headers.ContentType = new("application/json");
        var rejected = await authorized.SendAsync(signedAfterRevoke);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    private HttpClient CreateClient()
    {
        _factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Receiver:RootPath"] = _root,
                    ["Receiver:UploadToken"] = "integration-token",
                    ["Receiver:ChunkSizeBytes"] = (1024 * 1024).ToString(),
                    ["Receiver:MinimumFreeSpaceBytes"] = "0",
                    ["Receiver:MinimumFreeSpaceRatio"] = "0"
                });
            });
        });
        return _factory.CreateClient();
    }

    private static object CreateRequest(string sha256, int size) => new
    {
        file = new
        {
            deviceId = "android-integration",
            deviceName = "集成测试手机",
            backupDate = "2026-07-16",
            category = "Document",
            relativePath = "Documents/integration.txt",
            size,
            sha256,
            originalModifiedAt = "2026-07-16T00:00:00+08:00",
            isInitialBackup = false
        }
    };

    private static HttpRequestMessage CreateSignedRequest(
        HttpMethod method,
        string path,
        byte[] body,
        string deviceId,
        ECDsa key)
    {
        var request = new HttpRequestMessage(method, path);
        ApplySignature(request, body, deviceId, key);
        if (method != HttpMethod.Get)
        {
            request.Content = new ByteArrayContent(body);
        }
        return request;
    }

    private static void ApplySignature(
        HttpRequestMessage request,
        byte[] body,
        string deviceId,
        ECDsa key)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var contentHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        var path = request.RequestUri!.OriginalString;
        var canonical = Encoding.UTF8.GetBytes(
            $"{request.Method.Method}\n{path}\n{timestamp}\n{nonce}\n{contentHash}");
        var signature = key.SignData(
            canonical,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        request.Headers.Add("X-Device-Id", deviceId);
        request.Headers.Add("X-Timestamp", timestamp);
        request.Headers.Add("X-Nonce", nonce);
        request.Headers.Add("X-Content-Sha256", contentHash);
        request.Headers.Add("X-Signature", Convert.ToBase64String(signature));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_factory is not null) await _factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

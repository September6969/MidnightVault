using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MidnightVault.Desktop.Services;

public sealed record ControlDevice(
    string Id,
    string DisplayName,
    string PublicKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset? RevokedAt);

public sealed record ControlTask(
    string TaskId,
    string DeviceId,
    string Status,
    int SuccessCount,
    int SkippedCount,
    int FailedCount,
    long TotalBytes,
    string? ErrorCode,
    DateTimeOffset CompletedAt);

public sealed record ControlCommand(
    string Id,
    string DeviceId,
    string Type,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcknowledgedAt);

public sealed class ControlPlaneClient(
    DesktopSettingsStore settingsStore,
    DesktopCredentialStore credentialStore)
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.ControlPlaneUrl)) return false;
        try
        {
            using var response = await HttpClient.GetAsync(
                settings.ControlPlaneUrl.TrimEnd('/') + "/healthz",
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<ControlDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "/v1/devices", cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ControlDevice>>(
            JsonOptions,
            cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<ControlTask>> GetTasksAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, "/v1/tasks?limit=100", cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ControlTask>>(
            JsonOptions,
            cancellationToken) ?? [];
    }

    public async Task<ControlCommand> CreateBackupCommandAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, "/v1/commands", cancellationToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { deviceId, type = "backup-now" }, JsonOptions),
            Encoding.UTF8,
            "application/json");
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ControlCommand>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("控制面返回了无效指令。");
    }

    public async Task RevokeDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(
            HttpMethod.Delete,
            "/v1/devices/" + Uri.EscapeDataString(deviceId),
            cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var token = await credentialStore.ReadControlTokenAsync(cancellationToken)
            ?? throw new InvalidOperationException("尚未配置手机服务器控制令牌。");
        if (string.IsNullOrWhiteSpace(settings.ControlPlaneUrl))
        {
            throw new InvalidOperationException("尚未配置手机服务器地址。");
        }
        var request = new HttpRequestMessage(method, settings.ControlPlaneUrl.TrimEnd('/') + path);
        request.Headers.Authorization = new("Bearer", token);
        return request;
    }
}

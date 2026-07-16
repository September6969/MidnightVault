using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MidnightVault.Desktop.Services;

public sealed class ReceiverApiClient(
    DesktopSettingsStore settingsStore,
    DesktopCredentialStore credentialStore)
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<ReceiverHealth?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        try
        {
            return await HttpClient.GetFromJsonAsync<ReceiverHealth>(
                settings.ReceiverBaseUrl + "/healthz",
                JsonOptions,
                cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<LibraryPage> QueryLibraryAsync(
        string? query,
        LibraryCategory? category,
        int offset = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>
        {
            $"offset={Math.Max(0, offset)}",
            $"limit={Math.Clamp(limit, 1, 500)}"
        };
        if (!string.IsNullOrWhiteSpace(query))
        {
            parameters.Add("query=" + Uri.EscapeDataString(query.Trim()));
        }
        if (category is not null)
        {
            parameters.Add("category=" + Uri.EscapeDataString(category.Value.ToString()));
        }

        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Get,
            "/api/v1/library?" + string.Join('&', parameters),
            cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LibraryPage>(JsonOptions, cancellationToken)
            ?? new LibraryPage([], offset, limit, 0);
    }

    public async Task<LibrarySummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Get,
            "/api/v1/library/summary",
            cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LibrarySummary>(JsonOptions, cancellationToken)
            ?? new LibrarySummary(0, 0, 0, null);
    }

    public async Task<string> GetLocalOrCachedPathAsync(
        LibraryItem item,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.BackupRootPath) ||
            string.IsNullOrWhiteSpace(settings.CachePath))
        {
            throw new InvalidOperationException("尚未配置备份盘。");
        }

        var root = Path.GetFullPath(settings.BackupRootPath);
        var localPath = Path.GetFullPath(Path.Combine(root, item.RelativeStoredPath));
        if (!localPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("资料库路径越过了备份根目录。");
        }
        if (File.Exists(localPath)) return localPath;

        var previewCache = Path.Combine(settings.CachePath, "preview");
        Directory.CreateDirectory(previewCache);
        var cachedPath = Path.Combine(previewCache, item.Sha256 + item.Extension);
        if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length == item.Size)
        {
            return cachedPath;
        }

        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Get,
            $"/api/v1/library/{item.Id}/content",
            cancellationToken);
        using var response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var temporary = cachedPath + ".tmp";
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
        }
        File.Move(temporary, cachedPath, overwrite: true);
        return cachedPath;
    }

    public async Task<PairingSession> CreatePairingSessionAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Post,
            "/api/v1/pairing/sessions",
            cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PairingSession>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("接收端返回了无效的配对会话。");
    }

    public async Task<IReadOnlyList<PairedDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Get,
            "/api/v1/devices",
            cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<PairedDevice>>(
            JsonOptions,
            cancellationToken) ?? [];
    }

    public async Task RevokeDeviceAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        using var request = await CreateAuthorizedRequestAsync(
            HttpMethod.Delete,
            "/api/v1/devices/" + Uri.EscapeDataString(deviceId),
            cancellationToken);
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(
        HttpMethod method,
        string path,
        CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var token = await credentialStore.ReadUploadTokenAsync(cancellationToken)
            ?? throw new InvalidOperationException("尚未生成上传令牌。");
        var request = new HttpRequestMessage(method, settings.ReceiverBaseUrl + path);
        request.Headers.Add("X-MidnightVault-Token", token);
        return request;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

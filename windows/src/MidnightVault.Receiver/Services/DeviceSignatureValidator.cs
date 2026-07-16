using System.Security.Cryptography;
using System.Text;

namespace MidnightVault.Receiver.Services;

public sealed class DeviceSignatureValidator(CatalogStore catalog)
{
    private const long MaximumBodyBytes = 65L * 1024 * 1024;

    public async Task<bool> IsAuthorizedAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var deviceId = context.Request.Headers["X-Device-Id"].ToString();
        var timestampText = context.Request.Headers["X-Timestamp"].ToString();
        var nonce = context.Request.Headers["X-Nonce"].ToString();
        var signatureText = context.Request.Headers["X-Signature"].ToString();
        var contentHash = context.Request.Headers["X-Content-Sha256"].ToString();
        if (string.IsNullOrWhiteSpace(deviceId) ||
            nonce.Length is < 16 or > 128 ||
            !long.TryParse(timestampText, out var timestamp) ||
            string.IsNullOrWhiteSpace(signatureText) ||
            contentHash.Length != 64 ||
            !contentHash.All(Uri.IsHexDigit))
        {
            return false;
        }

        var requestTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if ((DateTimeOffset.UtcNow - requestTime).Duration() > TimeSpan.FromMinutes(5)) return false;
        var publicKeyText = await catalog.GetDevicePublicKeyAsync(deviceId, cancellationToken);
        if (string.IsNullOrWhiteSpace(publicKeyText)) return false;

        if (context.Request.ContentLength > MaximumBodyBytes) return false;
        var bufferedBody = new MemoryStream(
            context.Request.ContentLength is > 0 and <= int.MaxValue
                ? (int)context.Request.ContentLength.Value
                : 0);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaximumBodyBytes)
            {
                bufferedBody.Dispose();
                return false;
            }
            await bufferedBody.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        bufferedBody.Position = 0;
        var actualHash = await SHA256.HashDataAsync(bufferedBody, cancellationToken);
        bufferedBody.Position = 0;
        if (!CryptographicOperations.FixedTimeEquals(
            actualHash,
            Convert.FromHexString(contentHash)))
        {
            bufferedBody.Dispose();
            return false;
        }

        var canonical = Encoding.UTF8.GetBytes(
            $"{context.Request.Method}\n{context.Request.Path}{context.Request.QueryString}\n" +
            $"{timestampText}\n{nonce}\n{contentHash.ToLowerInvariant()}");
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyText), out _);
            var signature = Convert.FromBase64String(signatureText);
            if (!key.VerifyData(
                canonical,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence))
            {
                bufferedBody.Dispose();
                return false;
            }
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            bufferedBody.Dispose();
            return false;
        }

        var nonceAccepted = await catalog.TryUseNonceAsync(
            deviceId,
            nonce,
            requestTime.AddMinutes(10),
            cancellationToken);
        if (!nonceAccepted)
        {
            bufferedBody.Dispose();
            return false;
        }
        context.Request.Body = bufferedBody;
        context.Response.RegisterForDispose(bufferedBody);
        return true;
    }
}

public sealed class DeviceSignatureMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        DeviceSignatureValidator validator)
    {
        if (!await validator.IsAuthorizedAsync(context, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        await next(context);
    }
}

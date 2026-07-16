using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using MidnightVault.Receiver.Contracts;

namespace MidnightVault.Receiver.Services;

public sealed class PairingSessionStore(IOptions<ReceiverOptions> options, CatalogStore catalog)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new(StringComparer.Ordinal);

    public CreatePairingSessionResponse Create()
    {
        RemoveExpired();
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        _sessions[code] = expiresAt;
        return new CreatePairingSessionResponse(code, expiresAt);
    }

    public async Task<ClaimPairingResponse?> ClaimAsync(
        ClaimPairingRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId) ||
            string.IsNullOrWhiteSpace(request.DeviceName) ||
            string.IsNullOrWhiteSpace(request.PublicKey) ||
            !_sessions.TryRemove(request.Code, out var expiresAt) ||
            expiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var token = options.Value.UploadToken;
        if (string.IsNullOrWhiteSpace(token)) return null;
        await catalog.RegisterDeviceAsync(
            request.DeviceId.Trim(),
            request.DeviceName.Trim(),
            request.PublicKey.Trim(),
            cancellationToken);
        return new ClaimPairingResponse(token);
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var session in _sessions.Where(session => session.Value <= now))
        {
            _sessions.TryRemove(session.Key, out _);
        }
    }
}

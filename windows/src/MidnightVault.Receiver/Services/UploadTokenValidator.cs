using System.Security.Cryptography;
using System.Text;
using System.Net;
using Microsoft.Extensions.Options;

namespace MidnightVault.Receiver.Services;

public sealed class UploadTokenValidator(IOptions<ReceiverOptions> options)
{
    public bool IsAuthorized(HttpContext context)
    {
        var configuredToken = options.Value.UploadToken;
        if (string.IsNullOrWhiteSpace(configuredToken))
        {
            return context.Connection.RemoteIpAddress is null || IPAddress.IsLoopback(context.Connection.RemoteIpAddress);
        }

        if (!context.Request.Headers.TryGetValue("X-MidnightVault-Token", out var suppliedToken))
        {
            return false;
        }

        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(configuredToken));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken.ToString()));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

using System.Security.Cryptography;
using System.Text;

namespace MidnightVault.Desktop.Services;

public sealed class DesktopCredentialStore
{
    public async Task<string> GetOrCreateUploadTokenAsync(CancellationToken cancellationToken = default)
    {
        var current = await ReadUploadTokenAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(current)) return current;
        return await RotateUploadTokenAsync(cancellationToken);
    }

    public async Task<string?> ReadUploadTokenAsync(CancellationToken cancellationToken = default)
        => await ReadSecretAsync(UploadCredentialPath, UploadEntropy, cancellationToken);

    public async Task SaveControlTokenAsync(string token, CancellationToken cancellationToken = default)
        => await SaveSecretAsync(ControlCredentialPath, ControlEntropy, token, cancellationToken);

    public async Task<string?> ReadControlTokenAsync(CancellationToken cancellationToken = default)
        => await ReadSecretAsync(ControlCredentialPath, ControlEntropy, cancellationToken);

    private static async Task<string?> ReadSecretAsync(
        string path,
        byte[] entropy,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken);
        try
        {
            var plaintext = ProtectedData.Unprotect(
                protectedBytes,
                entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task<string> RotateUploadTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await SaveSecretAsync(UploadCredentialPath, UploadEntropy, token, cancellationToken);
        return token;
    }

    private static async Task SaveSecretAsync(
        string path,
        byte[] entropy,
        string secret,
        CancellationToken cancellationToken)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret.Trim()),
            entropy,
            DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(CredentialDirectory);
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private static readonly byte[] UploadEntropy = Encoding.UTF8.GetBytes("MidnightVault.UploadToken.v1");
    private static readonly byte[] ControlEntropy = Encoding.UTF8.GetBytes("MidnightVault.ControlToken.v1");

    private static string CredentialDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MidnightVault");

    private static string UploadCredentialPath => Path.Combine(CredentialDirectory, "upload-token.bin");

    private static string ControlCredentialPath => Path.Combine(CredentialDirectory, "control-token.bin");
}

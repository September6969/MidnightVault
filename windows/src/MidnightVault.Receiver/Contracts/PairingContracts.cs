namespace MidnightVault.Receiver.Contracts;

public sealed record CreatePairingSessionResponse(string Code, DateTimeOffset ExpiresAt);

public sealed record ClaimPairingRequest(
    string Code,
    string DeviceId,
    string DeviceName,
    string PublicKey);

public sealed record ClaimPairingResponse(string UploadToken);

public sealed record PairedDeviceResponse(
    string DeviceId,
    string DeviceName,
    DateTimeOffset PairedAt,
    DateTimeOffset LastSeenAt);

public sealed record RegisterUploadDeviceRequest(
    string DeviceId,
    string DeviceName,
    string PublicKey);

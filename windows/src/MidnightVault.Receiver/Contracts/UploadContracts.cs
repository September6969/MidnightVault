using MidnightVault.Core.Models;

namespace MidnightVault.Receiver.Contracts;

public sealed record StartUploadRequest(UploadDescriptor File);

public sealed record StartUploadResponse(string UploadId, int ChunkSizeBytes, int ChunkCount);

public sealed record CompleteUploadResponse(string RelativeStoredPath, string Sha256, long Size);

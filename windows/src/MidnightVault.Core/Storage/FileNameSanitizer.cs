using System.Security.Cryptography;
using System.Text;

namespace MidnightVault.Core.Storage;

public static class FileNameSanitizer
{
    private const int MaxSegmentLength = 120;

    private static readonly HashSet<char> InvalidCharacters =
        Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string SanitizeSegment(string segment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        var builder = new StringBuilder(segment.Length);
        foreach (var character in segment.Normalize(NormalizationForm.FormC))
        {
            builder.Append(InvalidCharacters.Contains(character) || char.IsControl(character) ? '_' : character);
        }

        var sanitized = builder.ToString().TrimEnd(' ', '.');
        if (sanitized.Length == 0)
        {
            sanitized = "_";
        }

        var reservedNameStem = Path.GetFileNameWithoutExtension(sanitized);
        if (ReservedNames.Contains(reservedNameStem))
        {
            sanitized = $"_{sanitized}";
        }

        if (sanitized.Length <= MaxSegmentLength)
        {
            return sanitized;
        }

        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(segment)))[..8].ToLowerInvariant();
        var extension = Path.GetExtension(sanitized);
        var maximumExtensionLength = MaxSegmentLength - suffix.Length - 2;
        extension = TruncateWithoutSplittingSurrogate(extension, maximumExtensionLength);

        var stem = Path.GetFileNameWithoutExtension(sanitized);
        if (stem.Length == 0)
        {
            stem = "_";
        }

        var allowedStemLength = Math.Max(1, MaxSegmentLength - extension.Length - suffix.Length - 1);
        stem = TruncateWithoutSplittingSurrogate(stem, allowedStemLength);
        return $"{stem}~{suffix}{extension}";
    }

    private static string TruncateWithoutSplittingSurrogate(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }

        var length = maximumLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }
}

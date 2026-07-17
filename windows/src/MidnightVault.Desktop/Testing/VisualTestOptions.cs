namespace MidnightVault.Desktop.Testing;

internal static class VisualTestOptions
{
    internal static bool Enabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("MIDNIGHTVAULT_VISUAL_TEST"),
            "1",
            StringComparison.Ordinal);

    internal static string Page =>
        Environment.GetEnvironmentVariable("MIDNIGHTVAULT_VISUAL_PAGE")?.Trim().ToLowerInvariant()
        ?? "dashboard";

    internal static string Theme =>
        Environment.GetEnvironmentVariable("MIDNIGHTVAULT_VISUAL_THEME")?.Trim().ToLowerInvariant()
        ?? "system";

    internal static int Width => ReadPositiveInt("MIDNIGHTVAULT_VISUAL_WIDTH", 1280);

    internal static int Height => ReadPositiveInt("MIDNIGHTVAULT_VISUAL_HEIGHT", 720);

    internal static int RasterScalePercent =>
        Math.Clamp(ReadPositiveInt("MIDNIGHTVAULT_VISUAL_SCALE", 100), 100, 200);

    internal static string? OutputPath
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("MIDNIGHTVAULT_VISUAL_OUTPUT");
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value.Trim());
        }
    }

    private static int ReadPositiveInt(string name, int fallback)
    {
        return int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0
            ? value
            : fallback;
    }
}

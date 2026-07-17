namespace MidnightVault.Desktop.DesignSystem;

internal enum DesktopLayoutMode
{
    Compact,
    Medium,
    Expanded,
}

internal static class AdaptiveLayout
{
    internal static DesktopLayoutMode Classify(double availableWidth) => availableWidth switch
    {
        < GeneratedDesignTokens.CompactBreakpoint => DesktopLayoutMode.Compact,
        < GeneratedDesignTokens.ExpandedBreakpoint => DesktopLayoutMode.Medium,
        _ => DesktopLayoutMode.Expanded,
    };
}

package win.september.midnightvault.ui.adaptive

enum class VaultWindowSize {
    Compact,
    Medium,
    Expanded
}

fun classifyWindowWidth(widthDp: Int): VaultWindowSize = when {
    widthDp < VaultMediumBreakpointDp -> VaultWindowSize.Compact
    widthDp < VaultExpandedBreakpointDp -> VaultWindowSize.Medium
    else -> VaultWindowSize.Expanded
}

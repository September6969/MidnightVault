package win.september.midnightvault.ui.adaptive

import org.junit.Assert.assertEquals
import org.junit.Test

class VaultWindowSizeTest {
    @Test
    fun widthBreakpointsFollowSharedTokens() {
        assertEquals(VaultWindowSize.Compact, classifyWindowWidth(359))
        assertEquals(VaultWindowSize.Compact, classifyWindowWidth(599))
        assertEquals(VaultWindowSize.Medium, classifyWindowWidth(600))
        assertEquals(VaultWindowSize.Medium, classifyWindowWidth(839))
        assertEquals(VaultWindowSize.Expanded, classifyWindowWidth(840))
        assertEquals(VaultWindowSize.Expanded, classifyWindowWidth(1280))
    }
}

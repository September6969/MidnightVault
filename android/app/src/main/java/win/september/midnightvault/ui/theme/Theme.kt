package win.september.midnightvault.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.text.TextStyle

private val MidnightLightColors = lightColorScheme(
    primary = GeneratedVaultColors.AccentLight,
    onPrimary = GeneratedVaultColors.OnAccentLight,
    primaryContainer = GeneratedVaultColors.PrimaryContainerLight,
    onPrimaryContainer = GeneratedVaultColors.OnPrimaryContainerLight,
    secondary = GeneratedVaultColors.SecondaryLight,
    onSecondary = GeneratedVaultColors.OnSecondaryLight,
    background = GeneratedVaultColors.BackgroundLight,
    onBackground = GeneratedVaultColors.TextPrimaryLight,
    surface = GeneratedVaultColors.SurfaceLight,
    onSurface = GeneratedVaultColors.TextPrimaryLight,
    surfaceVariant = GeneratedVaultColors.SurfaceVariantLight,
    onSurfaceVariant = GeneratedVaultColors.TextSecondaryLight,
    error = GeneratedVaultColors.ErrorLight,
    onError = GeneratedVaultColors.OnErrorLight,
    outline = GeneratedVaultColors.OutlineLight,
    outlineVariant = GeneratedVaultColors.OutlineVariantLight
)

private val MidnightDarkColors = darkColorScheme(
    primary = GeneratedVaultColors.AccentDark,
    onPrimary = GeneratedVaultColors.OnAccentDark,
    primaryContainer = GeneratedVaultColors.PrimaryContainerDark,
    onPrimaryContainer = GeneratedVaultColors.OnPrimaryContainerDark,
    secondary = GeneratedVaultColors.SecondaryDark,
    onSecondary = GeneratedVaultColors.OnSecondaryDark,
    background = GeneratedVaultColors.BackgroundDark,
    onBackground = GeneratedVaultColors.TextPrimaryDark,
    surface = GeneratedVaultColors.SurfaceDark,
    onSurface = GeneratedVaultColors.TextPrimaryDark,
    surfaceVariant = GeneratedVaultColors.SurfaceVariantDark,
    onSurfaceVariant = GeneratedVaultColors.TextSecondaryDark,
    error = GeneratedVaultColors.ErrorDark,
    onError = GeneratedVaultColors.OnErrorDark,
    outline = GeneratedVaultColors.OutlineDark,
    outlineVariant = GeneratedVaultColors.OutlineVariantDark
)

private val MidnightTypography = Typography(
    headlineMedium = TextStyle(
        fontSize = GeneratedVaultTypography.HeadlineMediumSize,
        lineHeight = GeneratedVaultTypography.HeadlineMediumLineHeight,
        fontWeight = GeneratedVaultTypography.HeadlineMediumWeight
    ),
    headlineSmall = TextStyle(
        fontSize = GeneratedVaultTypography.HeadlineSmallSize,
        lineHeight = GeneratedVaultTypography.HeadlineSmallLineHeight,
        fontWeight = GeneratedVaultTypography.HeadlineSmallWeight
    ),
    titleLarge = TextStyle(
        fontSize = GeneratedVaultTypography.TitleLargeSize,
        lineHeight = GeneratedVaultTypography.TitleLargeLineHeight,
        fontWeight = GeneratedVaultTypography.TitleLargeWeight
    ),
    titleMedium = TextStyle(
        fontSize = GeneratedVaultTypography.TitleMediumSize,
        lineHeight = GeneratedVaultTypography.TitleMediumLineHeight,
        fontWeight = GeneratedVaultTypography.TitleMediumWeight
    ),
    bodyLarge = TextStyle(
        fontSize = GeneratedVaultTypography.BodyLargeSize,
        lineHeight = GeneratedVaultTypography.BodyLargeLineHeight,
        fontWeight = GeneratedVaultTypography.BodyLargeWeight
    ),
    bodyMedium = TextStyle(
        fontSize = GeneratedVaultTypography.BodyMediumSize,
        lineHeight = GeneratedVaultTypography.BodyMediumLineHeight,
        fontWeight = GeneratedVaultTypography.BodyMediumWeight
    ),
    labelLarge = TextStyle(
        fontSize = GeneratedVaultTypography.LabelLargeSize,
        lineHeight = GeneratedVaultTypography.LabelLargeLineHeight,
        fontWeight = GeneratedVaultTypography.LabelLargeWeight
    )
)

private val MidnightShapes = Shapes(
    small = androidx.compose.foundation.shape.RoundedCornerShape(GeneratedVaultSize.SmallRadius),
    medium = androidx.compose.foundation.shape.RoundedCornerShape(GeneratedVaultSize.MediumRadius),
    large = androidx.compose.foundation.shape.RoundedCornerShape(GeneratedVaultSize.LargeRadius)
)

@Composable
fun MidnightVaultTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = if (darkTheme) MidnightDarkColors else MidnightLightColors,
        typography = MidnightTypography,
        shapes = MidnightShapes,
        content = content
    )
}

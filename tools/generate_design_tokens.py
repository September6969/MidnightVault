import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TOKEN_PATH = ROOT / "design-system" / "design-tokens.json"
DESKTOP_ROOT = ROOT / "windows" / "src" / "MidnightVault.Desktop"
ANDROID_UI_ROOT = (
    ROOT
    / "android"
    / "app"
    / "src"
    / "main"
    / "java"
    / "win"
    / "september"
    / "midnightvault"
    / "ui"
)


def color_with_alpha(color: str, alpha: str) -> str:
    return f"#{alpha}{color.removeprefix('#')}"


def kotlin_color(color: str) -> str:
    return f"0xFF{color.removeprefix('#')}"


def build_xaml(tokens: dict) -> str:
    colors = tokens["color"]
    spacing = tokens["spacing"]
    radius = tokens["radius"]
    typography = tokens["typography"]
    layout = tokens["layout"]
    control = tokens["control"]
    motion = tokens["motion"]
    desktop = tokens["breakpoints"]["desktop"]
    return f'''<?xml version="1.0" encoding="utf-8"?>
<!-- 此文件由 tools/generate_design_tokens.py 生成，请修改 design-system/design-tokens.json。 -->
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Light">
            <SolidColorBrush x:Key="MidnightAccentBrush" Color="{colors['accent']['light']}" />
            <SolidColorBrush x:Key="MidnightAccentStrongBrush" Color="{colors['accentStrong']['light']}" />
            <SolidColorBrush x:Key="MidnightAccentSoftBrush" Color="{color_with_alpha(colors['accent']['light'], '14')}" />
            <SolidColorBrush x:Key="MidnightHeroBackgroundBrush" Color="{color_with_alpha(colors['accent']['light'], '0F')}" />
            <SolidColorBrush x:Key="MidnightHeroStrokeBrush" Color="{color_with_alpha(colors['accent']['light'], '4D')}" />
            <SolidColorBrush x:Key="MidnightSuccessBrush" Color="{colors['success']['light']}" />
            <SolidColorBrush x:Key="MidnightWarningBrush" Color="{colors['warning']['light']}" />
            <SolidColorBrush x:Key="MidnightErrorBrush" Color="{colors['error']['light']}" />
            <SolidColorBrush x:Key="MidnightOnAccentBrush" Color="{colors['onAccent']['light']}" />
        </ResourceDictionary>
        <ResourceDictionary x:Key="Dark">
            <SolidColorBrush x:Key="MidnightAccentBrush" Color="{colors['accent']['dark']}" />
            <SolidColorBrush x:Key="MidnightAccentStrongBrush" Color="{colors['accentStrong']['dark']}" />
            <SolidColorBrush x:Key="MidnightAccentSoftBrush" Color="{color_with_alpha(colors['accent']['dark'], '1F')}" />
            <SolidColorBrush x:Key="MidnightHeroBackgroundBrush" Color="{color_with_alpha(colors['accent']['dark'], '17')}" />
            <SolidColorBrush x:Key="MidnightHeroStrokeBrush" Color="{color_with_alpha(colors['accent']['dark'], '66')}" />
            <SolidColorBrush x:Key="MidnightSuccessBrush" Color="{colors['success']['dark']}" />
            <SolidColorBrush x:Key="MidnightWarningBrush" Color="{colors['warning']['dark']}" />
            <SolidColorBrush x:Key="MidnightErrorBrush" Color="{colors['error']['dark']}" />
            <SolidColorBrush x:Key="MidnightOnAccentBrush" Color="{colors['onAccent']['dark']}" />
        </ResourceDictionary>
        <ResourceDictionary x:Key="HighContrast">
            <SolidColorBrush x:Key="MidnightAccentBrush" Color="{{ThemeResource SystemColorHighlightColor}}" />
            <SolidColorBrush x:Key="MidnightAccentStrongBrush" Color="{{ThemeResource SystemColorHighlightColor}}" />
            <SolidColorBrush x:Key="MidnightAccentSoftBrush" Color="{{ThemeResource SystemColorButtonFaceColor}}" />
            <SolidColorBrush x:Key="MidnightHeroBackgroundBrush" Color="{{ThemeResource SystemColorWindowColor}}" />
            <SolidColorBrush x:Key="MidnightHeroStrokeBrush" Color="{{ThemeResource SystemColorHighlightColor}}" />
            <SolidColorBrush x:Key="MidnightSuccessBrush" Color="{{ThemeResource SystemColorHighlightColor}}" />
            <SolidColorBrush x:Key="MidnightWarningBrush" Color="{{ThemeResource SystemColorHighlightColor}}" />
            <SolidColorBrush x:Key="MidnightErrorBrush" Color="{{ThemeResource SystemColorHotlightColor}}" />
            <SolidColorBrush x:Key="MidnightOnAccentBrush" Color="{{ThemeResource SystemColorHighlightTextColor}}" />
        </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>

    <x:Double x:Key="MidnightCompactBreakpoint">{desktop['mediumMin']}</x:Double>
    <x:Double x:Key="MidnightExpandedBreakpoint">{desktop['expandedMin']}</x:Double>
    <x:Double x:Key="MidnightDesktopMinWidth">{layout['desktopMinimumWidth']}</x:Double>
    <x:Double x:Key="MidnightDesktopMinHeight">{layout['desktopMinimumHeight']}</x:Double>
    <x:Double x:Key="MidnightPageMaxWidth">{layout['desktopContentMaxWidth']}</x:Double>
    <x:Double x:Key="MidnightPageTitleFontSize">{typography['pageTitle']}</x:Double>
    <x:Double x:Key="MidnightSectionTitleFontSize">{typography['sectionTitle']}</x:Double>
    <x:Double x:Key="MidnightMetricFontSize">{typography['metric']}</x:Double>
    <x:Double x:Key="MidnightBodyFontSize">{typography['body']}</x:Double>
    <x:Double x:Key="MidnightDesktopControlHeight">{control['desktopHeight']}</x:Double>
    <x:Double x:Key="MidnightIconSmall">{control['iconSmall']}</x:Double>
    <x:Double x:Key="MidnightIconMedium">{control['iconMedium']}</x:Double>
    <x:Double x:Key="MidnightIconLarge">{control['iconLarge']}</x:Double>
    <x:Double x:Key="MidnightMotionFast">{motion['fastMs']}</x:Double>
    <x:Double x:Key="MidnightMotionNormal">{motion['normalMs']}</x:Double>
    <x:Double x:Key="MidnightMotionSlow">{motion['slowMs']}</x:Double>
    <Thickness x:Key="MidnightPagePadding">{spacing['desktopPage']},28,{spacing['desktopPage']},40</Thickness>
    <Thickness x:Key="MidnightCompactPagePadding">24,24,24,36</Thickness>
    <Thickness x:Key="MidnightPhonePagePadding">20,22,20,32</Thickness>
    <CornerRadius x:Key="MidnightCardCornerRadius">{radius['small']}</CornerRadius>
</ResourceDictionary>
'''


def build_csharp(tokens: dict) -> str:
    desktop = tokens["breakpoints"]["desktop"]
    layout = tokens["layout"]
    light_background = tokens["color"]["background"]["light"].removeprefix("#")
    dark_background = tokens["color"]["background"]["dark"].removeprefix("#")
    return f'''// 此文件由 tools/generate_design_tokens.py 生成，请修改 design-system/design-tokens.json。
namespace MidnightVault.Desktop.DesignSystem;

internal static class GeneratedDesignTokens
{{
    internal const double CompactBreakpoint = {desktop['mediumMin']};
    internal const double ExpandedBreakpoint = {desktop['expandedMin']};
    internal const int MinimumWindowWidth = {layout['desktopMinimumWidth']};
    internal const int MinimumWindowHeight = {layout['desktopMinimumHeight']};
    internal static readonly Windows.UI.Color LightBackground = Windows.UI.Color.FromArgb(
        0xFF, 0x{light_background[0:2]}, 0x{light_background[2:4]}, 0x{light_background[4:6]});
    internal static readonly Windows.UI.Color DarkBackground = Windows.UI.Color.FromArgb(
        0xFF, 0x{dark_background[0:2]}, 0x{dark_background[2:4]}, 0x{dark_background[4:6]});
}}
'''


def build_android_tokens(tokens: dict) -> str:
    colors = tokens["color"]
    spacing = tokens["spacing"]
    radius = tokens["radius"]
    control = tokens["control"]
    motion = tokens["motion"]
    layout = tokens["layout"]
    typography = tokens["typography"]["android"]
    return f'''// 此文件由 tools/generate_design_tokens.py 生成，请修改 design-system/design-tokens.json。
package win.september.midnightvault.ui.theme

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

internal object GeneratedVaultColors {{
    val AccentLight = Color({kotlin_color(colors['accent']['light'])})
    val AccentDark = Color({kotlin_color(colors['accent']['dark'])})
    val OnAccentLight = Color({kotlin_color(colors['onAccent']['light'])})
    val OnAccentDark = Color({kotlin_color(colors['onAccent']['dark'])})
    val PrimaryContainerLight = Color({kotlin_color(colors['primaryContainer']['light'])})
    val PrimaryContainerDark = Color({kotlin_color(colors['primaryContainer']['dark'])})
    val OnPrimaryContainerLight = Color({kotlin_color(colors['onPrimaryContainer']['light'])})
    val OnPrimaryContainerDark = Color({kotlin_color(colors['onPrimaryContainer']['dark'])})
    val SecondaryLight = Color({kotlin_color(colors['secondary']['light'])})
    val SecondaryDark = Color({kotlin_color(colors['secondary']['dark'])})
    val OnSecondaryLight = Color({kotlin_color(colors['onSecondary']['light'])})
    val OnSecondaryDark = Color({kotlin_color(colors['onSecondary']['dark'])})
    val BackgroundLight = Color({kotlin_color(colors['background']['light'])})
    val BackgroundDark = Color({kotlin_color(colors['background']['dark'])})
    val TextPrimaryLight = Color({kotlin_color(colors['textPrimary']['light'])})
    val TextPrimaryDark = Color({kotlin_color(colors['textPrimary']['dark'])})
    val TextSecondaryLight = Color({kotlin_color(colors['textSecondary']['light'])})
    val TextSecondaryDark = Color({kotlin_color(colors['textSecondary']['dark'])})
    val SurfaceLight = Color({kotlin_color(colors['surface']['light'])})
    val SurfaceDark = Color({kotlin_color(colors['surface']['dark'])})
    val SurfaceVariantLight = Color({kotlin_color(colors['surfaceVariant']['light'])})
    val SurfaceVariantDark = Color({kotlin_color(colors['surfaceVariant']['dark'])})
    val ErrorLight = Color({kotlin_color(colors['error']['light'])})
    val ErrorDark = Color({kotlin_color(colors['error']['dark'])})
    val OnErrorLight = Color({kotlin_color(colors['onError']['light'])})
    val OnErrorDark = Color({kotlin_color(colors['onError']['dark'])})
    val OutlineLight = Color({kotlin_color(colors['outline']['light'])})
    val OutlineDark = Color({kotlin_color(colors['outline']['dark'])})
    val OutlineVariantLight = Color({kotlin_color(colors['outlineVariant']['light'])})
    val OutlineVariantDark = Color({kotlin_color(colors['outlineVariant']['dark'])})
}}

internal object GeneratedVaultSpacing {{
    val ExtraSmall = {spacing['xs']}.dp
    val Small = {spacing['sm']}.dp
    val Medium = {spacing['md']}.dp
    val Large = {spacing['lg']}.dp
    val ExtraLarge = {spacing['xl']}.dp
    val ExtraExtraLarge = {spacing['xxl']}.dp
}}

internal object GeneratedVaultSize {{
    val TouchTarget = {control['androidTouchTarget']}.dp
    val CompactPagePadding = {spacing['androidCompactPage']}.dp
    val ExpandedPagePadding = {spacing['androidExpandedPage']}.dp
    val ContentMaxWidth = {layout['androidContentMaxWidth']}.dp
    val ExpandedSidebarWidth = {layout['androidExpandedSidebarWidth']}.dp
    val HeroIllustration = {layout['androidHeroIllustration']}.dp
    val SmallRadius = {radius['small']}.dp
    val MediumRadius = {radius['medium']}.dp
    val LargeRadius = {radius['large']}.dp
}}

internal object GeneratedVaultMotion {{
    const val Fast = {motion['fastMs']}
    const val Normal = {motion['normalMs']}
    const val Slow = {motion['slowMs']}
}}

internal object GeneratedVaultTypography {{
    val HeadlineMediumSize = {typography['headlineMedium']['size']}.sp
    val HeadlineMediumLineHeight = {typography['headlineMedium']['lineHeight']}.sp
    val HeadlineMediumWeight = FontWeight.{typography['headlineMedium']['weight']}
    val HeadlineSmallSize = {typography['headlineSmall']['size']}.sp
    val HeadlineSmallLineHeight = {typography['headlineSmall']['lineHeight']}.sp
    val HeadlineSmallWeight = FontWeight.{typography['headlineSmall']['weight']}
    val TitleLargeSize = {typography['titleLarge']['size']}.sp
    val TitleLargeLineHeight = {typography['titleLarge']['lineHeight']}.sp
    val TitleLargeWeight = FontWeight.{typography['titleLarge']['weight']}
    val TitleMediumSize = {typography['titleMedium']['size']}.sp
    val TitleMediumLineHeight = {typography['titleMedium']['lineHeight']}.sp
    val TitleMediumWeight = FontWeight.{typography['titleMedium']['weight']}
    val BodyLargeSize = {typography['bodyLarge']['size']}.sp
    val BodyLargeLineHeight = {typography['bodyLarge']['lineHeight']}.sp
    val BodyLargeWeight = FontWeight.{typography['bodyLarge']['weight']}
    val BodyMediumSize = {typography['bodyMedium']['size']}.sp
    val BodyMediumLineHeight = {typography['bodyMedium']['lineHeight']}.sp
    val BodyMediumWeight = FontWeight.{typography['bodyMedium']['weight']}
    val LabelLargeSize = {typography['labelLarge']['size']}.sp
    val LabelLargeLineHeight = {typography['labelLarge']['lineHeight']}.sp
    val LabelLargeWeight = FontWeight.{typography['labelLarge']['weight']}
}}
'''


def build_android_adaptive(tokens: dict) -> str:
    android = tokens["breakpoints"]["android"]
    return f'''// 此文件由 tools/generate_design_tokens.py 生成，请修改 design-system/design-tokens.json。
package win.september.midnightvault.ui.adaptive

internal const val VaultMediumBreakpointDp = {android['mediumMin']}
internal const val VaultExpandedBreakpointDp = {android['expandedMin']}
'''


def main() -> None:
    tokens = json.loads(TOKEN_PATH.read_text(encoding="utf-8"))
    outputs = {
        DESKTOP_ROOT / "Styles" / "GeneratedDesignTokens.xaml": build_xaml(tokens),
        DESKTOP_ROOT / "DesignSystem" / "GeneratedDesignTokens.cs": build_csharp(tokens),
        ANDROID_UI_ROOT / "theme" / "GeneratedVaultTokens.kt": build_android_tokens(tokens),
        ANDROID_UI_ROOT / "adaptive" / "GeneratedAdaptiveTokens.kt": build_android_adaptive(tokens),
    }
    for path, content in outputs.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="\n")
        print(f"[design-tokens] 已输出 {path}")


if __name__ == "__main__":
    main()

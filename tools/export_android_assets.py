import json
from pathlib import Path

from PIL import Image, ImageDraw
from slice_asset_sheet import (
    chroma_to_alpha,
    clear_chroma_residue,
    content_bbox,
    fit_to_canvas,
    normalize_sheet,
    parse_hex_color,
    sample_border_key,
)


ROOT = Path(__file__).resolve().parents[1]
ANDROID_RES = ROOT / "android" / "app" / "src" / "main" / "res"
DESIGN_TOKEN_PATH = ROOT / "design-system" / "design-tokens.json"
ILLUSTRATION_MANIFEST = (
    ROOT
    / "windows"
    / "src"
    / "MidnightVault.Desktop"
    / "Assets"
    / "Generated"
    / "Manifests"
    / "empty-state-atlas-01.json"
)
ILLUSTRATION_SHEET = (
    ROOT
    / "windows"
    / "src"
    / "MidnightVault.Desktop"
    / "Assets"
    / "Generated"
    / "SourceSheets"
    / "empty-state-atlas-01.png"
)


def fit_transparent(source: Image.Image, size: int, content_ratio: float) -> Image.Image:
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    target = max(1, round(size * content_ratio))
    scale = min(target / source.width, target / source.height)
    image = source.resize(
        (max(1, round(source.width * scale)), max(1, round(source.height * scale))),
        Image.Resampling.LANCZOS,
    )
    x = (size - image.width) // 2
    y = (size - image.height) // 2
    canvas.alpha_composite(image, (x, y))
    return canvas


def load_high_resolution_illustration() -> Image.Image:
    manifest = json.loads(ILLUSTRATION_MANIFEST.read_text(encoding="utf-8"))
    canvas = manifest["canvas"]
    grid = manifest["grid"]
    item = next(entry for entry in manifest["items"] if entry["name"] == "empty-backup")
    configured_key = parse_hex_color(manifest["background"]["color"])
    tolerance = int(manifest["background"]["tolerance"])

    with Image.open(ILLUSTRATION_SHEET) as source_image:
        sheet = normalize_sheet(source_image, int(canvas["width"]), int(canvas["height"]))
    key = sample_border_key(sheet, configured_key)
    left = int(item["column"]) * int(grid["cellWidth"])
    top = int(item["row"]) * int(grid["cellHeight"])
    cell = sheet.crop(
        (left, top, left + int(grid["cellWidth"]), top + int(grid["cellHeight"]))
    )
    cleaned = clear_chroma_residue(chroma_to_alpha(cell, key, tolerance), key)
    bounds = content_bbox(cleaned)
    if bounds is None:
        raise ValueError("empty-backup 高分辨率切片为空")
    return fit_to_canvas(cleaned, bounds, 640, 640, 60)


def save_webp(image: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, "WEBP", lossless=True, method=6)
    print(f"[android-assets] 已输出 {path}")


def build_assets() -> None:
    source = load_high_resolution_illustration()
    design_tokens = json.loads(DESIGN_TOKEN_PATH.read_text(encoding="utf-8"))
    launcher_background = design_tokens["color"]["background"]["dark"]
    launcher_stroke = design_tokens["color"]["accent"]["dark"]
    densities = {
        "mdpi": 160,
        "hdpi": 240,
        "xhdpi": 320,
        "xxhdpi": 480,
        "xxxhdpi": 640,
    }
    for density, size in densities.items():
        image = fit_transparent(source, size, 0.92)
        save_webp(
            image,
            ANDROID_RES / f"drawable-{density}" / "vault_backup_hero.webp",
        )

    launcher_sizes = {
        "mdpi": 108,
        "hdpi": 162,
        "xhdpi": 216,
        "xxhdpi": 324,
        "xxxhdpi": 432,
    }
    for density, size in launcher_sizes.items():
        foreground = fit_transparent(source, size, 0.58)
        save_webp(
            foreground,
            ANDROID_RES / f"mipmap-{density}" / "ic_launcher_foreground.webp",
        )

        legacy_size = round(size * 48 / 108)
        legacy = Image.new("RGBA", (legacy_size, legacy_size), launcher_background)
        draw = ImageDraw.Draw(legacy)
        stroke = max(1, round(legacy_size * 0.035))
        draw.rounded_rectangle(
            (stroke, stroke, legacy_size - stroke - 1, legacy_size - stroke - 1),
            radius=round(legacy_size * 0.2),
            outline=launcher_stroke,
            width=stroke,
        )
        art = fit_transparent(source, legacy_size, 0.72)
        legacy.alpha_composite(art)
        save_webp(legacy, ANDROID_RES / f"mipmap-{density}" / "ic_launcher.webp")


if __name__ == "__main__":
    build_assets()

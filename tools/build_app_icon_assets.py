#!/usr/bin/env python3
"""由已切割的 MidnightVault 插画生成 Windows 打包与标题栏图标。"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


BACKGROUND = (8, 24, 38, 255)
ACCENT = (91, 184, 232, 255)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="生成 MidnightVault Windows 图标资产")
    parser.add_argument(
        "--project-dir",
        type=Path,
        default=Path("windows/src/MidnightVault.Desktop"),
        help="桌面客户端项目目录",
    )
    return parser.parse_args()


def rounded_background(size: tuple[int, int], radius_ratio: float = 0.18) -> Image.Image:
    image = Image.new("RGBA", size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    radius = max(2, round(min(size) * radius_ratio))
    inset = max(1, round(min(size) * 0.035))
    draw.rounded_rectangle(
        (inset, inset, size[0] - inset - 1, size[1] - inset - 1),
        radius=radius,
        fill=BACKGROUND,
        outline=ACCENT,
        width=max(1, round(min(size) * 0.025)),
    )
    return image


def place_subject(canvas: Image.Image, subject: Image.Image, scale: float) -> None:
    available = (max(1, round(canvas.width * scale)), max(1, round(canvas.height * scale)))
    resized = subject.copy()
    resized.thumbnail(available, Image.Resampling.LANCZOS)
    origin = ((canvas.width - resized.width) // 2, (canvas.height - resized.height) // 2)
    canvas.alpha_composite(resized, origin)


def render_square(subject: Image.Image, size: int, with_background: bool = True) -> Image.Image:
    canvas = rounded_background((size, size)) if with_background else Image.new(
        "RGBA", (size, size), (0, 0, 0, 0)
    )
    place_subject(canvas, subject, 0.78 if with_background else 0.9)
    return canvas


def render_wide(subject: Image.Image, width: int, height: int) -> Image.Image:
    canvas = Image.new("RGBA", (width, height), BACKGROUND)
    place_subject(canvas, subject, 0.72)
    return canvas


def main() -> int:
    args = parse_args()
    project_dir = args.project_dir.resolve()
    assets_dir = project_dir / "Assets"
    processed_dir = assets_dir / "Generated" / "Processed"
    drive = Image.open(processed_dir / "drive-setup.png").convert("RGBA")
    backup = Image.open(processed_dir / "empty-backup.png").convert("RGBA")

    outputs = {
        "StoreLogo.png": render_square(drive, 50),
        "Square44x44Logo.scale-200.png": render_square(drive, 88),
        "Square44x44Logo.targetsize-24_altform-unplated.png": render_square(drive, 24, False),
        "Square44x44Logo.targetsize-48_altform-lightunplated.png": render_square(drive, 48, False),
        "Square150x150Logo.scale-200.png": render_square(backup, 300),
        "Wide310x150Logo.scale-200.png": render_wide(backup, 620, 300),
        "SplashScreen.scale-200.png": render_wide(backup, 1240, 600),
        "LockScreenLogo.scale-200.png": render_square(drive, 48, False),
    }
    for name, image in outputs.items():
        path = assets_dir / name
        image.save(path, format="PNG", optimize=True)
        print(f"[app-icon] 已输出 {path}")

    ico_source = render_square(drive, 256)
    ico_path = assets_dir / "AppIcon.ico"
    ico_source.save(
        ico_path,
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print(f"[app-icon] 已输出 {ico_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

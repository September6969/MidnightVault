import argparse
import json
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageStat


def validate_image(path: Path) -> Image.Image:
    image = Image.open(path).convert("RGBA")
    if image.width < 320 or image.height < 240:
        raise ValueError(f"截图尺寸异常：{image.width}x{image.height}")
    extrema = ImageStat.Stat(image.convert("RGB")).extrema
    if all(high - low < 8 for low, high in extrema):
        raise ValueError("截图几乎为纯色，拒绝作为视觉基线")
    alpha = image.getchannel("A")
    if ImageStat.Stat(alpha).mean[0] < 250:
        raise ValueError("截图包含大面积透明区域，拒绝作为视觉基线")
    return image


def main() -> int:
    parser = argparse.ArgumentParser(description="验证或比较 UI PNG 视觉基线")
    parser.add_argument("current", type=Path)
    parser.add_argument("reference", type=Path, nargs="?")
    parser.add_argument("--validate-only", action="store_true")
    parser.add_argument("--pixel-threshold", type=int, default=12)
    parser.add_argument("--max-changed-ratio", type=float, default=0.01)
    parser.add_argument("--expected-theme", choices=("light", "dark"))
    args = parser.parse_args()

    current = validate_image(args.current)
    luminance_histogram = current.convert("RGB").convert("L").histogram()
    near_black_ratio = sum(luminance_histogram[:16]) / (current.width * current.height)
    if args.expected_theme == "light" and near_black_ratio > 0.08:
        raise ValueError(
            f"浅色截图黑色像素异常：{near_black_ratio:.2%}，可能尚未完成合成"
        )
    result: dict[str, object] = {
        "current": str(args.current),
        "size": [current.width, current.height],
        "valid": True,
        "nearBlackRatio": near_black_ratio,
    }
    if args.validate_only:
        print(json.dumps(result, ensure_ascii=False))
        return 0

    if args.reference is None:
        parser.error("比较模式需要 reference")
    reference = validate_image(args.reference)
    if current.size != reference.size:
        result.update(
            passed=False,
            reason=f"尺寸不同：current={current.size}, reference={reference.size}",
        )
        print(json.dumps(result, ensure_ascii=False))
        return 1

    difference = ImageChops.difference(current.convert("RGB"), reference.convert("RGB"))
    changed = difference.convert("L").point(
        lambda value: 255 if value > args.pixel_threshold else 0
    )
    histogram = changed.histogram()
    changed_pixels = histogram[255]
    total_pixels = current.width * current.height
    changed_ratio = changed_pixels / total_pixels
    passed = changed_ratio <= args.max_changed_ratio
    result.update(
        reference=str(args.reference),
        changedPixels=changed_pixels,
        totalPixels=total_pixels,
        changedRatio=changed_ratio,
        maxChangedRatio=args.max_changed_ratio,
        pixelThreshold=args.pixel_threshold,
        passed=passed,
    )
    print(json.dumps(result, ensure_ascii=False))
    return 0 if passed else 1


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exception:
        print(json.dumps({"passed": False, "error": str(exception)}, ensure_ascii=False))
        raise SystemExit(2)

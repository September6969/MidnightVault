#!/usr/bin/env python3
"""按 manifest 固定网格切割 GUI 图集，并生成质量报告与多尺寸预览。"""

from __future__ import annotations

import argparse
import json
import math
import statistics
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFont


PREVIEW_SIZES = (16, 24, 32, 48, 64, 128)


@dataclass(frozen=True)
class SliceResult:
    name: str
    path: Path
    coverage: float
    content_bbox: tuple[int, int, int, int]
    touches_edge: bool
    chroma_residue_pixels: int
    hash_value: int


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="切割 MidnightVault GUI 素材图集")
    parser.add_argument("--manifest", required=True, type=Path, help="图集 manifest 路径")
    parser.add_argument("--source", type=Path, help="覆盖 manifest 推导出的源图路径")
    parser.add_argument("--output-dir", type=Path, help="覆盖默认独立素材目录")
    parser.add_argument("--preview-dir", type=Path, help="覆盖默认预览与报告目录")
    return parser.parse_args()


def log(message: str) -> None:
    print(f"[asset-slicer] {message}")


def parse_hex_color(value: str) -> tuple[int, int, int]:
    normalized = value.lstrip("#")
    if len(normalized) != 6:
        raise ValueError(f"不支持的色键格式: {value}")
    return tuple(int(normalized[index:index + 2], 16) for index in (0, 2, 4))


def load_manifest(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as file:
        manifest = json.load(file)

    grid = manifest["grid"]
    items = manifest["items"]
    expected_count = int(grid["columns"]) * int(grid["rows"])
    if len(items) != expected_count:
        raise ValueError(f"manifest 项目数 {len(items)} 与网格单元数 {expected_count} 不一致")

    names = [item["name"] for item in items]
    if len(names) != len(set(names)):
        raise ValueError("manifest 中存在重复素材名称")

    indexes = [int(item["index"]) for item in items]
    if indexes != list(range(len(items))):
        raise ValueError("manifest index 必须从 0 连续递增")
    return manifest


def normalize_sheet(source: Image.Image, width: int, height: int) -> Image.Image:
    if source.width * height != source.height * width:
        raise ValueError(
            f"源图比例 {source.width}x{source.height} 与目标画布 {width}x{height} 不一致"
        )
    if source.size == (width, height):
        return source.convert("RGBA")

    log(f"将源图从 {source.width}x{source.height} 规范化为 {width}x{height}")
    return source.convert("RGBA").resize((width, height), Image.Resampling.LANCZOS)


def sample_border_key(image: Image.Image, configured_key: tuple[int, int, int]) -> tuple[int, int, int]:
    """从画布边缘取中位色，修正模型输出对目标色键的轻微偏差。"""
    step = max(1, min(image.width, image.height) // 160)
    samples: list[tuple[int, int, int]] = []
    for x in range(0, image.width, step):
        samples.append(image.getpixel((x, 0))[:3])
        samples.append(image.getpixel((x, image.height - 1))[:3])
    for y in range(0, image.height, step):
        samples.append(image.getpixel((0, y))[:3])
        samples.append(image.getpixel((image.width - 1, y))[:3])

    sampled_key = tuple(round(statistics.median(channel)) for channel in zip(*samples))
    if math.dist(sampled_key, configured_key) > 64:
        raise ValueError(
            f"边缘采样色 {sampled_key} 与 manifest 色键 {configured_key} 差异过大"
        )
    log(f"色键边缘采样值为 {sampled_key}")
    return sampled_key


def chroma_to_alpha(
    image: Image.Image,
    key: tuple[int, int, int],
    tolerance: int,
) -> Image.Image:
    """按 RGB 欧氏距离生成软蒙版，并反算半透明边缘以清理色键溢色。"""
    transparent_distance = max(52.0, tolerance * 1.25)
    opaque_distance = max(transparent_distance + 1.0, tolerance * 3.1)
    output: list[tuple[int, int, int, int]] = []

    for red, green, blue, _ in image.get_flattened_data():
        distance = math.sqrt(
            (red - key[0]) ** 2
            + (green - key[1]) ** 2
            + (blue - key[2]) ** 2
        )
        if distance <= transparent_distance:
            output.append((0, 0, 0, 0))
            continue
        if distance >= opaque_distance:
            output.append((red, green, blue, 255))
            continue

        normalized = (distance - transparent_distance) / (opaque_distance - transparent_distance)
        alpha = int(round((normalized * normalized * (3.0 - 2.0 * normalized)) * 255.0))
        alpha_fraction = max(alpha / 255.0, 0.08)

        # 根据色键合成公式反算前景，避免抗锯齿边缘残留洋红。
        clean_red = round((red - (1.0 - alpha_fraction) * key[0]) / alpha_fraction)
        clean_green = round((green - (1.0 - alpha_fraction) * key[1]) / alpha_fraction)
        clean_blue = round((blue - (1.0 - alpha_fraction) * key[2]) / alpha_fraction)
        clean_pixel = (
            max(0, min(255, clean_red)),
            max(0, min(255, clean_green)),
            max(0, min(255, clean_blue)),
        )
        clean_key_distance = math.sqrt(
            (clean_pixel[0] - key[0]) ** 2
            + (clean_pixel[1] - key[1]) ** 2
            + (clean_pixel[2] - key[2]) ** 2
        )
        if clean_key_distance < 80:
            output.append((0, 0, 0, 0))
        else:
            output.append((*clean_pixel, alpha))

    result = Image.new("RGBA", image.size)
    result.putdata(output)
    return result


def content_bbox(image: Image.Image, threshold: int = 12) -> tuple[int, int, int, int] | None:
    alpha = image.getchannel("A").point(lambda value: 255 if value > threshold else 0)
    return alpha.getbbox()


def fit_to_canvas(
    image: Image.Image,
    bbox: tuple[int, int, int, int],
    width: int,
    height: int,
    padding: int,
) -> Image.Image:
    subject = image.crop(bbox)
    available_width = width - padding * 2
    available_height = height - padding * 2
    scale = min(available_width / subject.width, available_height / subject.height)
    resized_size = (
        max(1, round(subject.width * scale)),
        max(1, round(subject.height * scale)),
    )
    subject = subject.resize(resized_size, Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    origin = ((width - subject.width) // 2, (height - subject.height) // 2)
    canvas.alpha_composite(subject, origin)
    return canvas


def clear_chroma_residue(image: Image.Image, key: tuple[int, int, int]) -> Image.Image:
    """清除缩放插值再次产生的少量色键边缘像素。"""
    output: list[tuple[int, int, int, int]] = []
    for red, green, blue, alpha in image.get_flattened_data():
        if alpha == 0:
            output.append((0, 0, 0, 0))
            continue
        key_distance = math.sqrt(
            (red - key[0]) ** 2
            + (green - key[1]) ** 2
            + (blue - key[2]) ** 2
        )
        if key_distance < 105:
            output.append((0, 0, 0, 0))
        else:
            output.append((red, green, blue, alpha))
    cleaned = Image.new("RGBA", image.size)
    cleaned.putdata(output)
    return cleaned


def difference_hash(image: Image.Image) -> int:
    backdrop = Image.new("RGBA", image.size, (30, 33, 40, 255))
    backdrop.alpha_composite(image)
    grayscale = backdrop.convert("L").resize((9, 8), Image.Resampling.LANCZOS)
    pixels = list(grayscale.get_flattened_data())
    value = 0
    for row in range(8):
        for column in range(8):
            left = pixels[row * 9 + column]
            right = pixels[row * 9 + column + 1]
            value = (value << 1) | int(left > right)
    return value


def inspect_output(image: Image.Image, key: tuple[int, int, int]) -> tuple[float, tuple[int, int, int, int], bool, int]:
    bbox = content_bbox(image)
    if bbox is None:
        raise ValueError("输出素材接近全透明")

    alpha = image.getchannel("A")
    alpha_values = list(alpha.get_flattened_data())
    visible_count = sum(value > 12 for value in alpha_values)
    coverage = visible_count / (image.width * image.height)

    touches_edge = any(
        alpha.getpixel(point) > 12
        for point in (
            *((x, 0) for x in range(image.width)),
            *((x, image.height - 1) for x in range(image.width)),
            *((0, y) for y in range(image.height)),
            *((image.width - 1, y) for y in range(image.height)),
        )
    )

    residue_count = 0
    for red, green, blue, pixel_alpha in image.get_flattened_data():
        if pixel_alpha <= 32:
            continue
        key_distance = math.sqrt(
            (red - key[0]) ** 2
            + (green - key[1]) ** 2
            + (blue - key[2]) ** 2
        )
        if key_distance < 72:
            residue_count += 1
    return coverage, bbox, touches_edge, residue_count


def checkerboard(width: int, height: int, cell: int = 12) -> Image.Image:
    image = Image.new("RGBA", (width, height), (31, 34, 41, 255))
    draw = ImageDraw.Draw(image)
    colors = ((31, 34, 41, 255), (43, 47, 57, 255))
    for top in range(0, height, cell):
        for left in range(0, width, cell):
            color = colors[((left // cell) + (top // cell)) % 2]
            draw.rectangle((left, top, left + cell - 1, top + cell - 1), fill=color)
    return image


def generate_preview(results: list[SliceResult], preview_path: Path) -> None:
    label_width = 170
    cell_width = 150
    header_height = 42
    row_height = 166
    width = label_width + len(PREVIEW_SIZES) * cell_width
    height = header_height + len(results) * row_height
    preview = checkerboard(width, height)
    draw = ImageDraw.Draw(preview)
    font = ImageFont.load_default()

    draw.text((14, 14), "asset", font=font, fill=(230, 235, 242, 255))
    for column, size in enumerate(PREVIEW_SIZES):
        draw.text(
            (label_width + column * cell_width + 12, 14),
            f"{size}x{size}",
            font=font,
            fill=(230, 235, 242, 255),
        )

    for row, result in enumerate(results):
        top = header_height + row * row_height
        draw.text((14, top + 72), result.name, font=font, fill=(230, 235, 242, 255))
        source = Image.open(result.path).convert("RGBA")
        for column, size in enumerate(PREVIEW_SIZES):
            icon = source.resize((size, size), Image.Resampling.LANCZOS)
            left = label_width + column * cell_width + (cell_width - size) // 2
            icon_top = top + (row_height - size) // 2
            preview.alpha_composite(icon, (left, icon_top))

    preview_path.parent.mkdir(parents=True, exist_ok=True)
    preview.save(preview_path, format="PNG", optimize=True)


def main() -> int:
    args = parse_args()
    manifest_path = args.manifest.resolve()
    manifest = load_manifest(manifest_path)
    generated_root = manifest_path.parent.parent
    source_path = (args.source or generated_root / "SourceSheets" / manifest["sheet"]).resolve()
    output_dir = (args.output_dir or generated_root / "Processed").resolve()
    preview_dir = (args.preview_dir or generated_root / "Previews").resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    preview_dir.mkdir(parents=True, exist_ok=True)

    canvas = manifest["canvas"]
    grid = manifest["grid"]
    output = manifest["output"]
    configured_key = parse_hex_color(manifest["background"]["color"])
    tolerance = int(manifest["background"]["tolerance"])

    with Image.open(source_path) as source_image:
        sheet = normalize_sheet(source_image, int(canvas["width"]), int(canvas["height"]))
    key = sample_border_key(sheet, configured_key)

    results: list[SliceResult] = []
    for item in manifest["items"]:
        left = int(item["column"]) * int(grid["cellWidth"])
        top = int(item["row"]) * int(grid["cellHeight"])
        right = left + int(grid["cellWidth"])
        bottom = top + int(grid["cellHeight"])
        cell = sheet.crop((left, top, right, bottom))
        cleaned = chroma_to_alpha(cell, key, tolerance)
        cleaned = clear_chroma_residue(cleaned, key)
        bbox = content_bbox(cleaned)
        if bbox is None:
            raise ValueError(f"{item['name']} 切片为空")

        final = fit_to_canvas(
            cleaned,
            bbox,
            int(output["canvasWidth"]),
            int(output["canvasHeight"]),
            int(output["padding"]),
        )
        final = clear_chroma_residue(final, key)
        coverage, final_bbox, touches_edge, residue_count = inspect_output(final, key)
        if not 0.08 <= coverage <= 0.72:
            raise ValueError(f"{item['name']} 主体占比异常: {coverage:.1%}")
        if touches_edge:
            raise ValueError(f"{item['name']} 主体触碰输出边缘")
        if residue_count > 12:
            raise ValueError(f"{item['name']} 检测到 {residue_count} 个色键残留像素")

        output_path = output_dir / f"{item['name']}.png"
        final.save(output_path, format="PNG", optimize=True)
        result = SliceResult(
            name=item["name"],
            path=output_path,
            coverage=coverage,
            content_bbox=final_bbox,
            touches_edge=touches_edge,
            chroma_residue_pixels=residue_count,
            hash_value=difference_hash(final),
        )
        results.append(result)
        log(f"已输出 {output_path.name}，主体占比 {coverage:.1%}")

    duplicate_warnings: list[str] = []
    for index, left in enumerate(results):
        for right in results[index + 1:]:
            distance = (left.hash_value ^ right.hash_value).bit_count()
            if distance < 7:
                duplicate_warnings.append(
                    f"{left.name} 与 {right.name} 的差异哈希距离仅为 {distance}"
                )

    preview_path = preview_dir / f"{source_path.stem}-preview.png"
    generate_preview(results, preview_path)
    report_path = preview_dir / f"{source_path.stem}-report.json"
    report = {
        "source": str(source_path),
        "manifest": str(manifest_path),
        "preview": str(preview_path),
        "items": [
            {
                "name": result.name,
                "path": str(result.path),
                "coverage": round(result.coverage, 6),
                "contentBBox": result.content_bbox,
                "touchesEdge": result.touches_edge,
                "chromaResiduePixels": result.chroma_residue_pixels,
            }
            for result in results
        ],
        "duplicateWarnings": duplicate_warnings,
        "previewSizes": PREVIEW_SIZES,
    }
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    log(f"预览图: {preview_path}")
    log(f"质量报告: {report_path}")
    if duplicate_warnings:
        for warning in duplicate_warnings:
            log(f"警告: {warning}")
    else:
        log("未发现疑似重复素材")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

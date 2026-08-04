"""Render a QA montage from the same compact data consumed by WPF."""

from __future__ import annotations

import gzip
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from generate_fem_2d_overlay import read_nifti


def read_overlay(path: Path):
    raw = gzip.open(path, "rb").read()
    offset = 12
    mri_shape = struct.unpack_from("<3i", raw, offset); offset += 12
    field_shape = struct.unpack_from("<3i", raw, offset); offset += 12
    origin = np.asarray(struct.unpack_from("<3d", raw, offset)); offset += 24
    spacing = np.asarray(struct.unpack_from("<3d", raw, offset)); offset += 24
    maximum, minimum, threshold = struct.unpack_from("<3f", raw, offset); offset += 12
    slices = struct.unpack_from("<3i", raw, offset); offset += 12
    count = int(np.prod(field_shape))
    field = np.frombuffer(raw, "<u2", count=count, offset=offset).reshape(field_shape, order="F") / 65535 * maximum
    offset += count * 2
    roi = np.unpackbits(np.frombuffer(raw, "u1", offset=offset), bitorder="little")[: int(np.prod(mri_shape))].reshape(mri_shape, order="F").astype(bool)
    return field, roi, origin, spacing, maximum, minimum, threshold, slices


STOPS = np.asarray([[0, 48, 34, 136], [.18, 42, 92, 210], [.36, 23, 190, 210], [.54, 63, 213, 102], [.72, 220, 229, 60], [.87, 255, 139, 34], [1, 210, 30, 24]], float)


def colors(t):
    result = np.empty((*t.shape, 3), float)
    for i in range(1, len(STOPS)):
        selected = (t >= STOPS[i - 1, 0]) & (t <= STOPS[i, 0])
        weight = (t[selected] - STOPS[i - 1, 0]) / (STOPS[i, 0] - STOPS[i - 1, 0])
        result[selected] = STOPS[i - 1, 1:] * (1 - weight[:, None]) + STOPS[i, 1:] * weight[:, None]
    return result


def contour(mask):
    interior = mask.copy()
    interior[1:-1, 1:-1] &= mask[:-2, 1:-1] & mask[2:, 1:-1] & mask[1:-1, :-2] & mask[1:-1, 2:]
    return mask & ~interior


def sample_field(world, field, origin, spacing):
    grid = (world - origin) / spacing
    base = np.floor(grid).astype(int)
    fraction = grid - base
    valid = np.all((base >= 0) & (base < np.asarray(field.shape) - 1), axis=-1)
    base = np.clip(base, 0, np.asarray(field.shape) - 2)
    result = np.zeros(grid.shape[:-1], float)
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                weight = (fraction[..., 0] if dx else 1 - fraction[..., 0]) * (fraction[..., 1] if dy else 1 - fraction[..., 1]) * (fraction[..., 2] if dz else 1 - fraction[..., 2])
                result += field[base[..., 0] + dx, base[..., 1] + dy, base[..., 2] + dz] * weight
    result[~valid] = 0
    return result


def render_slice(t1, affine, field, roi, origin, spacing, maximum, minimum, threshold, kind, index):
    if kind == "sagittal":
        yy, zz = np.meshgrid(np.arange(t1.shape[1]), np.arange(t1.shape[2] - 1, -1, -1))
        xx = np.full_like(yy, index); volume_slice = t1[index, yy, zz]; roi_slice = roi[index, yy, zz]
    elif kind == "coronal":
        xx, zz = np.meshgrid(np.arange(t1.shape[0]), np.arange(t1.shape[2] - 1, -1, -1))
        yy = np.full_like(xx, index); volume_slice = t1[xx, index, zz]; roi_slice = roi[xx, index, zz]
    else:
        xx, yy = np.meshgrid(np.arange(t1.shape[0]), np.arange(t1.shape[1] - 1, -1, -1))
        zz = np.full_like(xx, index); volume_slice = t1[xx, yy, index]; roi_slice = roi[xx, yy, index]
    ijk = np.stack([xx, yy, zz, np.ones_like(xx)], axis=-1)
    world = ijk @ affine.T
    values = sample_field(world[..., :3], field, origin, spacing)
    low, high = np.quantile(t1[np.isfinite(t1)], [.01, .99])
    gray = np.clip((volume_slice - low) / (high - low), 0, 1)[..., None] * 255
    rgb = np.repeat(gray, 3, axis=-1)
    active = values >= minimum
    t = np.clip(values / maximum, 0, 1)
    alpha = (.25 + .43 * np.sqrt(t))[..., None]
    rgb[active] = rgb[active] * (1 - alpha[active]) + colors(t)[active] * alpha[active]
    rgb[contour(values >= threshold)] = [255, 45, 28]
    roi_edge = contour(roi_slice)
    roi_thick = roi_edge | np.roll(roi_edge, 1, 0) | np.roll(roi_edge, -1, 0) | np.roll(roi_edge, 1, 1) | np.roll(roi_edge, -1, 1)
    rgb[roi_thick] = [36, 255, 80]
    return Image.fromarray(np.clip(rgb, 0, 255).astype("u1"), "RGB")


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    t1, affine = read_nifti(root / "Assets/FemViewer/data/83Y04/T1.nii.gz")
    field, roi, origin, spacing, maximum, minimum, threshold, slices = read_overlay(root / "Assets/FemViewer/data/83Y04/83Y04-ti-amygdala.fem2d.gz")
    panels = [render_slice(t1, affine, field, roi, origin, spacing, maximum, minimum, threshold, kind, index) for kind, index in zip(("sagittal", "coronal", "axial"), slices)]
    canvas = Image.new("RGB", (1460, 510), "#090a0c")
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default(size=16)
    for i, (panel, title, index) in enumerate(zip(panels, ("SAGITTAL", "CORONAL", "AXIAL"), slices)):
        panel.thumbnail((420, 430), Image.Resampling.LANCZOS)
        x = 15 + i * 440; y = 45 + (430 - panel.height) // 2
        canvas.paste(panel, (x, y)); draw.text((x + 8, 12), f"{title} · voxel {index}", fill="#f0a33c", font=font)
    legend_x = 1365
    for y in range(60, 430):
        value = 1 - (y - 60) / 370
        color = tuple(colors(np.asarray([value]))[0].astype(int))
        draw.line((legend_x, y, legend_x + 18, y), fill=color)
    draw.rectangle((legend_x, 60, legend_x + 18, 430), outline="#697080")
    for value, y in zip((maximum, maximum * .75, maximum * .5, maximum * .25, 0), (55, 150, 245, 340, 420)):
        draw.text((legend_x + 25, y), f"{value:.3f}", fill="#d9dee7")
    draw.text((legend_x - 3, 18), "TI", fill="#d9dee7", font=font); draw.text((legend_x + 2, 452), "V/m", fill="#aeb5c2")
    draw.text((500, 484), "GREEN: bilateral amygdala ROI     RED: high-field contour", fill="#bfc6d1")
    output = root / "Tools/qa-output/fem-2d-preview.png"
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output)
    print(output)


if __name__ == "__main__":
    main()

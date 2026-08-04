"""Render a lightweight QA preview of the generated closed focus meshes."""

from __future__ import annotations

import base64
import json
import re
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]


def decode_mesh(item: dict) -> tuple[np.ndarray, np.ndarray]:
    vertices = np.frombuffer(base64.b64decode(item["v"]), dtype="<i2").reshape(-1, 3).astype(np.float64) / 20000.0
    faces = np.frombuffer(base64.b64decode(item["f"]), dtype="<u2").reshape(-1, 3).astype(np.int32)
    return vertices, faces


def rotation(yaw: float, pitch: float) -> np.ndarray:
    y, p = np.deg2rad((yaw, pitch))
    ry = np.asarray(((np.cos(y), 0, np.sin(y)), (0, 1, 0), (-np.sin(y), 0, np.cos(y))))
    rx = np.asarray(((1, 0, 0), (0, np.cos(p), -np.sin(p)), (0, np.sin(p), np.cos(p))))
    return rx @ ry


def rgb(hex_color: str) -> np.ndarray:
    return np.asarray(tuple(int(hex_color[i:i + 2], 16) for i in (1, 3, 5)), dtype=np.float64)


def render_panel(meshes: list[dict], yaw: float, pitch: float, size: int = 560) -> Image.Image:
    matrix = rotation(yaw, pitch)
    prepared = []
    all_xy = []
    light = np.asarray((-0.3, 0.55, 0.78))
    light /= np.linalg.norm(light)
    for mesh in meshes:
        vertices, faces = decode_mesh(mesh["data"])
        rotated = vertices @ matrix.T
        triangles = rotated[faces]
        normals = np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0])
        normal_length = np.linalg.norm(normals, axis=1)
        normals /= np.maximum(normal_length[:, None], 1e-12)
        illumination = np.clip(np.abs(normals @ light) * 0.65 + 0.35, 0.25, 1.0)
        depth = triangles[:, :, 2].mean(axis=1)
        prepared.append((triangles, depth, illumination, mesh))
        all_xy.append(rotated[:, :2])

    xy = np.vstack(all_xy)
    center = (xy.min(axis=0) + xy.max(axis=0)) * 0.5
    scale = (size - 52) / max(np.ptp(xy[:, 0]), np.ptp(xy[:, 1]))
    projected_meshes = []
    for triangles, depth, illumination, mesh in prepared:
        projected = (triangles[:, :, :2] - center) * scale
        projected[:, :, 0] += size / 2
        projected[:, :, 1] = size / 2 - projected[:, :, 1]
        projected_meshes.append((projected, depth, illumination, mesh))

    panel = Image.new("RGBA", (size, size), (13, 18, 25, 255))
    overlay = Image.new("RGBA", panel.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay, "RGBA")
    # Mirror the WebGL renderOrder: draw the transparent context first, then
    # the TI shells, and finally the target.  Sorting only within each closed
    # mesh avoids a misleading dark shell caused by cross-mesh alpha buildup.
    for projected, depth, illumination, mesh in projected_meshes:
        for index in np.argsort(depth):
            base = rgb(mesh["color"])
            shade = illumination[index]
            color = tuple(np.clip(base * shade + 18 * (1 - shade), 0, 255).astype(np.uint8))
            draw.polygon([tuple(point) for point in projected[index]], fill=(*color, mesh["alpha"]))
    return Image.alpha_composite(panel, overlay)


def main() -> None:
    atlas_text = (ROOT / "Assets/FemViewer/fem-atlas-meshes.js").read_text(encoding="utf-8")
    match = re.fullmatch(r"export const atlasMeshes=(\{.*\});\s*", atlas_text, re.S)
    if not match:
        raise ValueError("Atlas mesh module cannot be parsed")
    atlas = json.loads(match.group(1))
    anatomy = [region for region in atlas["regions"] if region["role"] == "anatomy"]
    target = next(region for region in atlas["regions"] if region["role"] == "target")
    meshes = [
        {"data": atlas["contextShell"], "color": atlas["contextShell"]["color"], "alpha": 14},
        {"data": atlas["whiteMatter"], "color": atlas["whiteMatter"]["color"], "alpha": 12},
        *({"data": region, "color": region["color"], "alpha": 34} for region in anatomy),
        {"data": target, "color": target["color"], "alpha": 150},
        {"data": atlas["fieldShells"][0], "color": atlas["fieldShells"][0]["color"], "alpha": 78},
        {"data": atlas["fieldShells"][1], "color": atlas["fieldShells"][1]["color"], "alpha": 230},
    ]
    views = ((-28, 8, "左前斜视"), (28, 8, "右前斜视"), (88, 4, "侧视"))
    panels = [render_panel(meshes, yaw, pitch) for yaw, pitch, _ in views]
    canvas = Image.new("RGB", (len(panels) * 560, 610), (9, 13, 19))
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.truetype("msyh.ttc", 22)
    for index, (panel, (_, _, title)) in enumerate(zip(panels, views)):
        canvas.paste(panel.convert("RGB"), (index * 560, 0))
        draw.text((index * 560 + 18, 570), title, fill=(225, 231, 239), font=font)
    output = ROOT / "Tools/qa-output/fem-atlas-focus-preview.png"
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output, quality=94)
    print(output)


if __name__ == "__main__":
    main()

"""Generate the compact, MRI-aligned FEM overlay bundled with the WPF viewer.

The source point cloud remains the authoritative simulation result.  This tool
only rasterizes it onto a small world-coordinate grid and packs the bilateral
amygdala mask so the desktop application can render slices without loading the
154 MB NPZ file.
"""

from __future__ import annotations

import argparse
import gzip
import math
import struct
from pathlib import Path

import numpy as np


DTYPES = {
    2: np.dtype("u1"),
    4: np.dtype("<i2"),
    8: np.dtype("<i4"),
    16: np.dtype("<f4"),
    64: np.dtype("<f8"),
    256: np.dtype("i1"),
    512: np.dtype("<u2"),
    768: np.dtype("<u4"),
}


def read_nifti(path: Path) -> tuple[np.ndarray, np.ndarray]:
    raw = gzip.open(path, "rb").read() if path.suffix.lower() == ".gz" else path.read_bytes()
    if len(raw) < 348 or struct.unpack_from("<i", raw, 0)[0] != 348:
        raise ValueError(f"Only little-endian NIfTI-1 is supported: {path}")

    dimensions = struct.unpack_from("<8h", raw, 40)
    shape = tuple(int(value) for value in dimensions[1:4])
    datatype = struct.unpack_from("<h", raw, 70)[0]
    dtype = DTYPES.get(datatype)
    if dtype is None:
        raise ValueError(f"Unsupported NIfTI datatype {datatype}: {path}")

    offset = max(348, int(struct.unpack_from("<f", raw, 108)[0]))
    count = math.prod(shape)
    data = np.frombuffer(raw, dtype=dtype, count=count, offset=offset).reshape(shape, order="F")
    slope = struct.unpack_from("<f", raw, 112)[0]
    intercept = struct.unpack_from("<f", raw, 116)[0]
    if not np.isfinite(slope) or slope == 0:
        slope = 1.0
    if not np.isfinite(intercept):
        intercept = 0.0
    data = data.astype(np.float32) * slope + intercept

    sform_code = struct.unpack_from("<h", raw, 254)[0]
    if sform_code > 0:
        affine = np.eye(4, dtype=np.float64)
        affine[:3, :] = np.asarray(struct.unpack_from("<12f", raw, 280), dtype=np.float64).reshape(3, 4)
    else:
        pixdim = struct.unpack_from("<8f", raw, 76)
        affine = np.diag([abs(pixdim[1]), abs(pixdim[2]), abs(pixdim[3]), 1.0])
    return data, affine


def build_overlay(field_path: Path, t1_path: Path, labels_path: Path, output_path: Path, spacing: float) -> None:
    t1, t1_affine = read_nifti(t1_path)
    labels, labels_affine = read_nifti(labels_path)
    if t1.shape != labels.shape or not np.allclose(t1_affine, labels_affine, atol=1e-3):
        raise ValueError("T1 and CHARM labeling grids are not aligned")

    with np.load(field_path) as archive:
        xyz_mm = np.asarray(archive["xyz"], dtype=np.float64) * 1000.0
        values = np.asarray(archive["values_ti_envelope"], dtype=np.float64)
    valid = np.isfinite(values) & np.all(np.isfinite(xyz_mm), axis=1)
    xyz_mm = xyz_mm[valid]
    values = values[valid]

    origin = np.floor(xyz_mm.min(axis=0) / spacing) * spacing
    upper = np.ceil(xyz_mm.max(axis=0) / spacing) * spacing
    field_shape = np.ceil((upper - origin) / spacing).astype(np.int32) + 1
    grid_indices = np.rint((xyz_mm - origin) / spacing).astype(np.int32)
    grid_indices = np.clip(grid_indices, 0, field_shape - 1)
    flat_indices = grid_indices[:, 0] + field_shape[0] * (grid_indices[:, 1] + field_shape[1] * grid_indices[:, 2])

    cell_count = int(np.prod(field_shape))
    value_sum = np.bincount(flat_indices, weights=values, minlength=cell_count)
    value_count = np.bincount(flat_indices, minlength=cell_count)
    field_grid = np.divide(value_sum, value_count, out=np.zeros(cell_count), where=value_count > 0).astype(np.float32)
    field_grid = field_grid.reshape(tuple(field_shape), order="F")

    # Close isolated raster holes while retaining zero outside the FEM domain.
    occupied = field_grid > 0
    for _ in range(2):
        neighbour_sum = np.zeros_like(field_grid)
        neighbour_hits = np.zeros(field_grid.shape, dtype=np.uint8)
        for axis in range(3):
            for shift in (-1, 1):
                shifted_values = np.roll(field_grid, shift, axis=axis)
                shifted_mask = np.roll(occupied, shift, axis=axis)
                boundary = [slice(None)] * 3
                boundary[axis] = 0 if shift == 1 else -1
                shifted_mask[tuple(boundary)] = False
                neighbour_sum += np.where(shifted_mask, shifted_values, 0)
                neighbour_hits += shifted_mask
        fill = ~occupied & (neighbour_hits >= 4)
        field_grid[fill] = neighbour_sum[fill] / neighbour_hits[fill]
        occupied |= fill

    display_max = float(np.quantile(values, 0.99))
    high_threshold = float(np.quantile(values, 0.95))
    display_low = float(np.quantile(values, 0.03))
    encoded_field = np.rint(np.clip(field_grid / display_max, 0, 1) * 65535.0).astype("<u2")

    left_roi = np.rint(labels).astype(np.int16) == 18
    right_roi = np.rint(labels).astype(np.int16) == 54
    roi = left_roi | right_roi
    if not roi.any():
        raise ValueError("Labels 18/54 (bilateral amygdala) were not found")
    roi_bits = np.packbits(roi.ravel(order="F"), bitorder="little")

    left_center = np.asarray(np.argwhere(left_roi).mean(axis=0)).round().astype(np.int32)
    roi_center = np.asarray(np.argwhere(roi).mean(axis=0)).round().astype(np.int32)
    default_sagittal = int(left_center[0])
    default_coronal = int(roi_center[1])
    default_axial = int(roi_center[2])

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with gzip.open(output_path, "wb", compresslevel=9) as output:
        output.write(b"FEM2D01\0")
        output.write(struct.pack("<i", 1))
        output.write(struct.pack("<3i", *t1.shape))
        output.write(struct.pack("<3i", *field_shape))
        output.write(struct.pack("<3d", *origin))
        output.write(struct.pack("<3d", spacing, spacing, spacing))
        output.write(struct.pack("<3f", display_max, display_low, high_threshold))
        output.write(struct.pack("<3i", default_sagittal, default_coronal, default_axial))
        output.write(encoded_field.tobytes(order="F"))
        output.write(roi_bits.tobytes())

    print(f"T1 grid: {t1.shape}; field grid: {tuple(field_shape)} at {spacing:g} mm")
    print(f"TI display range: {display_low:.6f} .. {display_max:.6f} V/m; contour: {high_threshold:.6f} V/m")
    print(f"Default slices (sagittal/coronal/axial): {default_sagittal}/{default_coronal}/{default_axial}")
    print(f"ROI voxels: left={left_roi.sum()}, right={right_roi.sum()}")
    print(f"Wrote {output_path} ({output_path.stat().st_size / 1024 / 1024:.2f} MiB)")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--field", type=Path, required=True)
    parser.add_argument("--t1", type=Path, required=True)
    parser.add_argument("--labels", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--spacing", type=float, default=3.0)
    args = parser.parse_args()
    build_overlay(args.field, args.t1, args.labels, args.output, args.spacing)


if __name__ == "__main__":
    main()

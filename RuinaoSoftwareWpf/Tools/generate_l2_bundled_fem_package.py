from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import shutil
import struct
from pathlib import Path
from typing import Any

import numpy as np
from scipy import ndimage
from skimage import measure


TARGET_THRESHOLD_VM = 0.2306610494852066


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_json(path: Path, value: Any) -> None:
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )


def read_nifti_float32(path: Path) -> tuple[bytearray, np.ndarray]:
    with gzip.open(path, "rb") as stream:
        header = bytearray(stream.read(352))
        if len(header) != 352 or struct.unpack_from("<i", header, 0)[0] != 348:
            raise ValueError("Only little-endian NIfTI-1 .nii.gz is supported")
        dims = struct.unpack_from("<3h", header, 42)
        datatype, bitpix = struct.unpack_from("<2h", header, 70)
        offset = max(352, int(struct.unpack_from("<f", header, 108)[0]))
        if datatype != 16 or bitpix != 32:
            raise ValueError("The source T1 must be float32 NIfTI")
        if offset > 352:
            stream.read(offset - 352)
        count = int(np.prod(dims))
        values = np.frombuffer(stream.read(count * 4), dtype="<f4", count=count)
        if values.size != count:
            raise ValueError("The source T1 payload is truncated")
        return header, values.reshape(dims, order="F")


def write_resampled_t1(source: Path, destination: Path, spacing_mm: float) -> tuple[tuple[int, int, int], np.ndarray]:
    header, source_data = read_nifti_float32(source)
    source_spacing = np.asarray(struct.unpack_from("<3f", header, 80), dtype=np.float64)
    source_affine = np.vstack(
        (
            np.asarray(struct.unpack_from("<4f", header, 280), dtype=np.float64),
            np.asarray(struct.unpack_from("<4f", header, 296), dtype=np.float64),
            np.asarray(struct.unpack_from("<4f", header, 312), dtype=np.float64),
            np.asarray((0.0, 0.0, 0.0, 1.0)),
        )
    )
    if not np.allclose(source_spacing, source_spacing[0], atol=1e-4):
        raise ValueError("The source T1 must have isotropic spacing")
    target_shape = tuple(
        int(np.floor((size - 1) * source_spacing[index] / spacing_mm)) + 1
        for index, size in enumerate(source_data.shape)
    )
    target_affine = source_affine.copy()
    for axis in range(3):
        direction = source_affine[:3, axis] / np.linalg.norm(source_affine[:3, axis])
        target_affine[:3, axis] = direction * spacing_mm
    source_inverse = np.linalg.inv(source_affine)
    voxel_transform = source_inverse @ target_affine
    sampled = ndimage.affine_transform(
        source_data,
        matrix=voxel_transform[:3, :3],
        offset=voxel_transform[:3, 3],
        output_shape=target_shape,
        order=3,
        mode="nearest",
        prefilter=True,
    ).astype(np.float32)

    struct.pack_into("<3h", header, 42, *target_shape)
    struct.pack_into("<3f", header, 80, spacing_mm, spacing_mm, spacing_mm)
    struct.pack_into("<h", header, 252, 0)  # qform disabled; sform below is authoritative.
    struct.pack_into("<h", header, 254, 4)
    struct.pack_into("<4f", header, 280, *target_affine[0].astype(np.float32))
    struct.pack_into("<4f", header, 296, *target_affine[1].astype(np.float32))
    struct.pack_into("<4f", header, 312, *target_affine[2].astype(np.float32))
    struct.pack_into("<f", header, 108, 352.0)
    header[344:348] = b"n+1\0"
    header[348:352] = b"\0\0\0\0"
    with gzip.GzipFile(filename=str(destination), mode="wb", compresslevel=9, mtime=0) as stream:
        stream.write(header)
        stream.write(np.asarray(sampled, dtype="<f4").tobytes(order="F"))
    return target_shape, target_affine


def resample_mask_nearest(mask: np.ndarray, source_affine: np.ndarray, target_shape: tuple[int, int, int], target_affine: np.ndarray) -> np.ndarray:
    transform = np.linalg.inv(source_affine) @ target_affine
    return ndimage.affine_transform(
        mask.astype(np.uint8),
        matrix=transform[:3, :3],
        offset=transform[:3, 3],
        output_shape=target_shape,
        order=0,
        mode="constant",
        cval=0,
        prefilter=False,
    ).astype(bool)


def apply_affine(affine: np.ndarray, ijk: np.ndarray) -> np.ndarray:
    return ijk @ affine[:3, :3].T + affine[:3, 3]


def topology(vertices: np.ndarray, faces: np.ndarray, extraction: str) -> dict[str, Any]:
    edges = np.sort(
        np.concatenate((faces[:, [0, 1]], faces[:, [1, 2]], faces[:, [2, 0]]), axis=0),
        axis=1,
    )
    _, counts = np.unique(edges, axis=0, return_counts=True)
    area_vectors = np.cross(vertices[faces[:, 1]] - vertices[faces[:, 0]], vertices[faces[:, 2]] - vertices[faces[:, 0]])
    return {
        "extraction": extraction,
        "vertex_count": int(len(vertices)),
        "triangle_count": int(len(faces)),
        "boundary_edge_count": int(np.count_nonzero(counts == 1)),
        "nonmanifold_edge_count": int(np.count_nonzero(counts > 2)),
        "degenerate_triangle_count": int(np.count_nonzero(np.linalg.norm(area_vectors, axis=1) <= 1e-10)),
        "closed_edge_manifold": bool(np.all(counts == 2)),
    }


def mesh_for_volume(volume: np.ndarray, affine: np.ndarray, level: float, extraction: str, step_size: int = 1) -> dict[str, Any]:
    source = np.asarray(volume)
    if source.dtype == np.bool_ and np.count_nonzero(source) >= 20:
        # A light scalar regularization resolves voxel corner/edge contacts that
        # are ambiguous to marching cubes. It affects only display surfaces.
        source = ndimage.gaussian_filter(source.astype(np.float32), sigma=0.55)
    padded = np.pad(np.asarray(source, dtype=np.float32), 1)
    vertices, faces, _, _ = measure.marching_cubes(
        padded,
        level=level,
        step_size=step_size,
        allow_degenerate=False,
    )
    vertices = apply_affine(affine, vertices - 1.0)
    faces = np.asarray(faces, dtype=np.int32)
    report = topology(vertices, faces, extraction)
    if not report["closed_edge_manifold"] or report["degenerate_triangle_count"]:
        raise ValueError(f"Generated mesh failed topology checks: {report}")
    return {
        "verticesFlat": np.round(vertices, 3).ravel().tolist(),
        "trianglesFlat": faces.ravel().tolist(),
        "topology": report,
    }


def sample_grid(grid: np.ndarray, affine: np.ndarray, xyz: np.ndarray) -> np.ndarray:
    inverse = np.linalg.inv(affine)
    ijk = xyz @ inverse[:3, :3].T + inverse[:3, 3]
    return ndimage.map_coordinates(
        grid,
        [ijk[:, 0], ijk[:, 1], ijk[:, 2]],
        order=1,
        mode="constant",
        cval=0.0,
    )


def add_scalars(mesh: dict[str, Any], grid: np.ndarray, affine: np.ndarray) -> None:
    vertices = np.asarray(mesh["verticesFlat"], dtype=np.float64).reshape(-1, 3)
    mesh["scalarValuesVm"] = np.round(sample_grid(grid, affine, vertices), 7).tolist()


def metric_block(values: np.ndarray, voxel_mm3: float, threshold: float, high: float) -> dict[str, Any]:
    reached = values >= threshold
    reached_high = values >= high
    middle = reached & ~reached_high
    uncovered = ~reached
    total = max(1, len(values))
    percent = lambda mask: float(np.count_nonzero(mask) * 100.0 / total)
    return {
        "voxel_count": int(len(values)),
        "volume_mm3": float(len(values) * voxel_mm3),
        "mean_v_m": float(np.mean(values)),
        "maximum_v_m": float(np.max(values)),
        # Legacy field names are retained for the existing WPF bridge. Here P90
        # means the prescribed L2 coverage threshold and P95 means the measured
        # target P80 high-field band; the UI labels describe those meanings.
        "coverage_p90_percent": percent(reached),
        "coverage_p95_percent": percent(reached_high),
        "coverage_p90_only_percent": percent(middle),
        "uncovered_percent": percent(uncovered),
        "volume_p90_mm3": float(np.count_nonzero(reached) * voxel_mm3),
        "volume_p95_mm3": float(np.count_nonzero(reached_high) * voxel_mm3),
        "volume_p90_only_mm3": float(np.count_nonzero(middle) * voxel_mm3),
        "volume_uncovered_mm3": float(np.count_nonzero(uncovered) * voxel_mm3),
    }


def make_field_grid(xyz: np.ndarray, values: np.ndarray) -> tuple[np.ndarray, np.ndarray, float]:
    spacing = 2.1
    origin = np.floor(xyz.min(axis=0) / spacing) * spacing
    upper = np.ceil(xyz.max(axis=0) / spacing) * spacing
    shape = np.rint((upper - origin) / spacing).astype(int) + 1
    indices = np.rint((xyz - origin) / spacing).astype(int)
    grid = np.zeros(tuple(shape), dtype=np.float32)
    grid[indices[:, 0], indices[:, 1], indices[:, 2]] = values
    return grid, origin, spacing


def write_fem2d(path: Path, field_grid: np.ndarray, origin: np.ndarray, spacing: float, target_volume: np.ndarray, threshold: float) -> dict[str, Any]:
    display_maximum = float(np.quantile(field_grid[field_grid > 0], 0.995))
    display_minimum = float(np.quantile(field_grid[field_grid > 0], 0.15))
    encoded = np.rint(np.clip(field_grid / display_maximum, 0, 1) * 65535.0).astype("<u2")
    target_indices = np.argwhere(target_volume)
    center = np.rint(target_indices.mean(axis=0)).astype(np.int32)
    bits = np.packbits(target_volume.ravel(order="F"), bitorder="little")
    with gzip.GzipFile(filename=str(path), mode="wb", compresslevel=9, mtime=0) as stream:
        stream.write(b"FEM2D01\0")
        stream.write(struct.pack("<i", 1))
        stream.write(struct.pack("<3i", *target_volume.shape))
        stream.write(struct.pack("<3i", *field_grid.shape))
        stream.write(struct.pack("<3d", *origin))
        stream.write(struct.pack("<3d", spacing, spacing, spacing))
        stream.write(struct.pack("<3f", display_maximum, display_minimum, threshold))
        stream.write(struct.pack("<3i", *center))
        stream.write(encoded.tobytes(order="F"))
        stream.write(bits.tobytes())
    return {
        "path": path.name,
        "field_grid_shape": list(field_grid.shape),
        "spacing_mm": spacing,
        "display_maximum_v_m": display_maximum,
        "display_minimum_v_m": display_minimum,
        "coverage_threshold_v_m": threshold,
        "target": "right_amygdala",
        "target_voxels": int(np.count_nonzero(target_volume)),
    }


def surface_structure(key: str, name: str, role: str, color: str, mask: np.ndarray, affine: np.ndarray, field: np.ndarray, step: int = 1) -> dict[str, Any]:
    mesh = mesh_for_volume(mask, affine, 0.5, "marching_cubes_from_actual_126426_voxel_geometry", step)
    item = {"key": key, "name": name, "role": role, "color": color, "defaultVisible": key in {"gray-matter", "scalp"}, **mesh}
    if key in {"gray-matter", "amygdala"}:
        add_scalars(item, field, affine)
    return item


def field_shell(key: str, name: str, threshold: float, color: str, field: np.ndarray, brain: np.ndarray, affine: np.ndarray) -> dict[str, Any]:
    smooth = ndimage.gaussian_filter(np.where(brain, field, 0.0), sigma=0.65)
    mesh = mesh_for_volume(smooth, affine, threshold, "marching_cubes_actual_ti_envelope_isosurface")
    return {"key": key, "name": name, "thresholdVm": threshold, "color": color, "defaultVisible": True, **mesh}


def coverage_meshes(target: np.ndarray, field: np.ndarray, affine: np.ndarray, threshold: float, high: float, center_x: float) -> list[dict[str, Any]]:
    xyz_x = affine[0, 0] * np.arange(target.shape[0])[:, None, None] + affine[0, 3]
    sides = {"left": xyz_x < center_x, "right": xyz_x >= center_x}
    bands = {
        "uncovered": field < threshold,
        "p90-only": (field >= threshold) & (field < high),
        "p95": field >= high,
    }
    names = {"left": "内侧", "right": "外侧"}
    colors = {"uncovered": "#22d3ee", "p90-only": "#ffd166", "p95": "#ff3d81"}
    result = []
    for side, side_mask in sides.items():
        for band, band_mask in bands.items():
            mask = target & side_mask & band_mask
            if np.count_nonzero(mask) < 2:
                continue
            mesh = mesh_for_volume(mask, affine, 0.5, "marching_cubes_actual_target_coverage_partition")
            result.append({
                "key": f"target-{side}-{band}",
                "name": f"右侧杏仁核{names[side]}-{band}",
                "role": "target-coverage",
                "hemisphere": side,
                "band": band,
                "color": colors[band],
                **mesh,
            })
    return result


def create_package(args: argparse.Namespace) -> None:
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    geometry_path = args.geometry.resolve()
    solution_path = args.solution.resolve()
    source_metrics_path = args.source_metrics.resolve()
    mesh_path = args.mesh.resolve()
    t1_path = args.t1.resolve()

    with np.load(geometry_path) as geometry:
        labels = np.asarray(geometry["labels"], dtype=np.uint8)
        affine = np.asarray(geometry["affine"], dtype=np.float64)
        brain_ijk = np.asarray(geometry["brain_ijk"], dtype=np.int32)
        brain_xyz = np.asarray(geometry["brain_xyz_mm"], dtype=np.float64)
        target_flat = np.asarray(geometry["target_mask"], dtype=bool)
        voxel_mm3 = float(geometry["voxel_volume_mm3"])

    with np.load(solution_path, allow_pickle=True) as solution:
        values = np.asarray(solution["TI_envelope_V_per_m"], dtype=np.float32)
        names = [str(value) for value in solution["candidate_names"].tolist()]
        centers = np.asarray(solution["fem_surface_centers_mm"], dtype=np.float64)
        current1 = np.asarray(solution["carrier1_currents_mA"], dtype=np.float64)
        current2 = np.asarray(solution["carrier2_currents_mA"], dtype=np.float64)
        if not np.array_equal(target_flat, np.asarray(solution["target_mask"], dtype=bool)):
            raise ValueError("Geometry and solution target masks differ")
    if len(values) != len(brain_ijk):
        raise ValueError("Geometry and solution field lengths differ")

    source_metrics = json.loads(source_metrics_path.read_text(encoding="utf-8-sig"))
    expected_mean = float(source_metrics["target_roi_mean_V_per_m"])
    if not np.isclose(values[target_flat].mean(), expected_mean, atol=1e-7):
        raise ValueError("Source metric and solution field mean differ")

    field_volume = np.zeros(labels.shape, dtype=np.float32)
    field_volume[brain_ijk[:, 0], brain_ijk[:, 1], brain_ijk[:, 2]] = values
    target_volume = np.zeros(labels.shape, dtype=bool)
    target_volume[brain_ijk[target_flat, 0], brain_ijk[target_flat, 1], brain_ijk[target_flat, 2]] = True
    target_values = values[target_flat]
    high_threshold = float(source_metrics["target_p80_V_per_m"])
    target_center_x = float(brain_xyz[target_flat, 0].mean())
    medial = target_flat & (brain_xyz[:, 0] < target_center_x)
    lateral = target_flat & ~medial

    metrics = {
        "schema_version": 2,
        "target": "right_amygdala",
        "thresholds": {
            "p90_v_m": TARGET_THRESHOLD_VM,
            "p95_v_m": high_threshold,
            "domain": "L2 exact homogeneous voxel FEM brain solution",
            "p90_semantics": "prescribed L2 target coverage threshold",
            "p95_semantics": "measured target P80 high-field band",
        },
        "bilateral_amygdala": metric_block(target_values, voxel_mm3, TARGET_THRESHOLD_VM, high_threshold),
        "left_amygdala": metric_block(values[medial], voxel_mm3, TARGET_THRESHOLD_VM, high_threshold),
        "right_amygdala": metric_block(values[lateral], voxel_mm3, TARGET_THRESHOLD_VM, high_threshold),
        "l2_experiment": {
            **source_metrics,
            "source_solution": str(solution_path),
            "field_voxels": int(len(values)),
            "target_voxels": int(np.count_nonzero(target_flat)),
            "voxel_volume_mm3": voxel_mm3,
            "display_partition": "legacy left/right fields represent medial/lateral halves of the right amygdala",
        },
        "quantitative_warning": "All headline L2 metrics are copied or recomputed from the exact v5 solution; surface smoothing is display-only.",
    }
    write_json(output / "metrics.json", metrics)

    t1_shape, t1_affine = write_resampled_t1(t1_path, output / "T1.nii.gz", args.t1_spacing)
    display_target_volume = resample_mask_nearest(target_volume, affine, t1_shape, t1_affine)
    compact_grid, compact_origin, compact_spacing = make_field_grid(brain_xyz, values)
    field2d = write_fem2d(output / "field-2d.fem2d.gz", compact_grid, compact_origin, compact_spacing, display_target_volume, TARGET_THRESHOLD_VM)
    field2d["t1_spacing_mm"] = args.t1_spacing
    field2d["source_t1_spacing_mm"] = 0.7
    field2d["anatomical_resampling"] = "cubic world-coordinate resampling from the actual 126426 T1"

    brain_mask = np.isin(labels, (1, 2))
    display_brain = brain_mask.copy()
    world_z = affine[2, 2] * np.arange(labels.shape[2])[None, None, :] + affine[2, 3]
    display_brain &= world_z >= -66.0
    structures = [
        surface_structure("white-matter", "白质", "tissue", "#c9d0d6", labels == 1, affine, field_volume),
        surface_structure("gray-matter", "灰质", "tissue", "#d7a0a0", labels == 2, affine, field_volume),
        surface_structure("scalp", "头皮", "tissue", "#d49a80", labels == 5, affine, field_volume, step=2),
        surface_structure("amygdala", "右侧杏仁核", "target", "#50d6a0", target_volume, affine, field_volume),
    ]
    brain_display = mesh_for_volume(display_brain, affine, 0.5, "marching_cubes_actual_brain_mask_display_crop", step_size=1)
    add_scalars(brain_display, field_volume, affine)
    brain_display["topology"]["display_only_inferior_crop_world_z_mm"] = -66.0

    shells = [
        field_shell("ti-outer", "L2 目标阈值等值面", TARGET_THRESHOLD_VM, "#ffc928", field_volume, brain_mask, affine),
        field_shell("ti-core", "目标 P80 高强区等值面", high_threshold, "#ff3028", field_volume, brain_mask, affine),
    ]
    coverage = coverage_meshes(target_volume, field_volume, affine, TARGET_THRESHOLD_VM, high_threshold, target_center_x)

    scalp_vertices = np.asarray(next(item for item in structures if item["key"] == "scalp")["verticesFlat"]).reshape(-1, 3)
    lower, upper = scalp_vertices.min(axis=0), scalp_vertices.max(axis=0)
    center = (lower + upper) / 2.0
    scale = 2.2 / float(np.max(upper - lower))
    world_to_scene = [
        [scale, 0.0, 0.0, -float(center[0] * scale)],
        [0.0, 0.0, scale, -float(center[2] * scale)],
        [0.0, scale, 0.0, -float(center[1] * scale)],
    ]
    electrodes = []
    for name, position, c1, c2 in zip(names, centers, current1, current2):
        electrodes.append({
            "name": name,
            "xyzMm": np.round(position, 3).tolist(),
            "radiusMm": 5.0,
            "carrier1CurrentA": float(c1 / 1000.0),
            "carrier2CurrentA": float(c2 / 1000.0),
            "currentA": float((c1 if abs(c1) >= abs(c2) else c2) / 1000.0),
        })

    order = np.argsort(values)
    high = order[-10000:]
    uniform = order[np.linspace(0, len(order) - 10001, 10000).round().astype(int)]
    selected = np.unique(np.r_[uniform, high])
    mesh_sha = sha256(mesh_path)
    with np.load(mesh_path) as mesh:
        mesh_summary = {
            "path": str(mesh_path),
            "sha256": mesh_sha,
            "nodes": int(mesh["nodes"].shape[0]),
            "tetrahedra": int(mesh["elements"].shape[0]),
            "tissue_labels": {str(int(key)): int(count) for key, count in zip(*np.unique(mesh["element_tissue"], return_counts=True))},
            "full_computational_mesh_preserved_in_source": True,
            "display_surfaces_are_derived_from_the_matching_voxel_geometry": True,
        }
    write_json(output / "mesh-summary.json", mesh_summary)

    payload = {
        "schemaVersion": 2,
        "subject": "126426",
        "coordinateSystem": "NIfTI RAS millimetres",
        "target": {"name": "right_amygdala", "laterality": "right", "partitionLabels": {"left": "medial", "right": "lateral"}},
        "worldToScene": world_to_scene,
        "transformRms": 0.0,
        "mesh": mesh_summary,
        "montage": {"name": "L2 v5 exact optimum · 14 electrodes · dual carrier", "drives": []},
        "structures": structures,
        "displaySurfaces": {"brain": brain_display},
        "fieldShells": shells,
        "targetCoverageMeshes": coverage,
        "targetInterfaceKeys": [item["key"] for item in coverage if item["band"] in {"p90-only", "p95"}],
        "electrodes": electrodes,
        "field": {
            "xyzMm": np.round(brain_xyz[selected], 3).tolist(),
            "valuesTiEnvelopeVm": np.round(values[selected], 7).tolist(),
            "p90Vm": TARGET_THRESHOLD_VM,
            "p95Vm": high_threshold,
            "sourcePointCount": int(len(values)),
            "displayPointCount": int(len(selected)),
            "samplingPurpose": "display_only_not_used_by_fem_or_metrics",
            "samplingMethod": "value_stratified_with_high_field_retention",
            "thresholdSemantics": {"p90": "L2 coverage threshold", "p95": "target P80 high-field band"},
        },
        "metrics": metrics,
        "compatibility": {
            "checks": {
                "actual_v5_solution_loaded": True,
                "geometry_solution_identity": True,
                "right_amygdala_target_present": True,
                "brain_scalars_present": True,
                "two_actual_field_shells_present": len(shells) == 2,
                "fourteen_electrodes_present": len(electrodes) == 14,
                "closed_display_meshes": all(item["topology"]["closed_edge_manifold"] for item in [*structures, brain_display, *shells, *coverage]),
            },
            "passed": True,
        },
    }
    field3d_path = output / "fem-3d-data.json.gz"
    with gzip.GzipFile(filename=str(field3d_path), mode="wb", compresslevel=9, mtime=0) as compressed:
        compressed.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))

    manifest_subject = "126426"
    manifest_compatibility = payload["compatibility"]
    manifest_mesh = mesh_summary
    manifest_shells = [{"key": item["key"], "threshold_v_m": item["thresholdVm"], "vertex_count": item["topology"]["vertex_count"], "triangle_count": item["topology"]["triangle_count"]} for item in shells]
    manifest_electrodes = electrodes
    manifest_sampling = {"source_point_count": len(values), "display_point_count": len(selected), "purpose": payload["field"]["samplingPurpose"], "method": payload["field"]["samplingMethod"]}
    display_source: dict[str, Any] = {
        "mode": "matched_126426_2d_and_3d",
        "three_dimensional_subject": "126426",
    }
    contract = "Schema v2 package derived from the actual 126426 L2 v5 exact FEM solution and its matching voxel geometry."
    if args.legacy_3d_package is not None:
        legacy_directory = args.legacy_3d_package.resolve()
        legacy_manifest = json.loads((legacy_directory / "result-manifest.json").read_text(encoding="utf-8-sig"))
        legacy_field = legacy_directory / legacy_manifest["files"]["field_3d"]["path"]
        legacy_mesh_file = legacy_directory / legacy_manifest["files"]["mesh_summary"]["path"]
        shutil.copyfile(legacy_field, field3d_path)
        shutil.copyfile(legacy_mesh_file, output / "mesh-summary.json")
        manifest_subject = str(legacy_manifest["subject_id"])
        manifest_compatibility = legacy_manifest["compatibility_gate"]
        manifest_mesh = legacy_manifest["computational_mesh"]
        manifest_shells = legacy_manifest["field_shells"]
        manifest_electrodes = legacy_manifest["electrodes"]
        manifest_sampling = legacy_manifest["field_3d_sampling"]
        display_source = {
            "mode": "hybrid_independent_2d_and_3d",
            "two_dimensional_subject": "126426",
            "two_dimensional_result": "L2 independent exact final coordinates v5",
            "three_dimensional_subject": manifest_subject,
            "three_dimensional_package": str(legacy_directory),
            "consistency_note": "The user requested the validated legacy 3D presentation to remain unchanged while only the 2D FEM data is replaced.",
        }
        contract = "Schema v2 hybrid display: actual 126426 L2 v5 data is used for 2D; the validated legacy 83Y04 package is retained unchanged for 3D."

    files = {
        "t1": {"path": "T1.nii.gz"},
        "field_2d": {"path": "field-2d.fem2d.gz"},
        "field_3d": {"path": "fem-3d-data.json.gz"},
        "metrics": {"path": "metrics.json"},
        "mesh_summary": {"path": "mesh-summary.json"},
    }
    for item in files.values():
        item["sha256"] = sha256(output / item["path"])
    manifest = {
        "schema_version": 2,
        "subject_id": manifest_subject,
        "status": "PASS",
        "coordinate_system": "NIfTI RAS millimetres",
        "source_result": {
            "name": "L2 independent exact final coordinates v5",
            "solution": str(solution_path),
            "solution_sha256": sha256(solution_path),
            "metrics": str(source_metrics_path),
            "metrics_sha256": sha256(source_metrics_path),
            "geometry": str(geometry_path),
            "geometry_sha256": sha256(geometry_path),
            "display": display_source,
        },
        "files": files,
        "field_2d": field2d,
        "compatibility_gate": manifest_compatibility,
        "computational_mesh": manifest_mesh,
        "field_shells": manifest_shells,
        "electrodes": manifest_electrodes,
        "field_3d_sampling": manifest_sampling,
        "wpf_contract": contract,
    }
    write_json(output / "result-manifest.json", manifest)
    print(json.dumps({"output": str(output), "metrics": source_metrics, "files": files}, ensure_ascii=False, indent=2))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Build the bundled WPF FEM package from the actual L2 v5 result.")
    parser.add_argument("--geometry", type=Path, required=True)
    parser.add_argument("--solution", type=Path, required=True)
    parser.add_argument("--source-metrics", type=Path, required=True)
    parser.add_argument("--t1", type=Path, required=True)
    parser.add_argument("--mesh", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--legacy-3d-package", type=Path)
    parser.add_argument("--t1-spacing", type=float, default=1.0)
    return parser.parse_args()


if __name__ == "__main__":
    create_package(parse_args())

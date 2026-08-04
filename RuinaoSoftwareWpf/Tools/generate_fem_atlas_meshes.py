"""Generate closed atlas-region and TI isosurface meshes for the 83Y04 viewer.

Only NumPy is required.  Marching tetrahedra is used instead of runtime
fragment-discard cutaways so every displayed focus object is a closed surface.
"""

from __future__ import annotations

import argparse
import base64
import gzip
import json
import re
import struct
from collections import deque
from pathlib import Path

import numpy as np

from generate_fem_2d_overlay import read_nifti


TETS = ((0, 5, 1, 6), (0, 1, 2, 6), (0, 2, 3, 6), (0, 3, 7, 6), (0, 7, 4, 6), (0, 4, 5, 6))
CORNERS = np.asarray(((0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0), (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)), dtype=np.int32)
TET_EDGES = ((0, 1), (1, 2), (2, 0), (0, 3), (1, 3), (2, 3))
TRI_TABLE = (
    (), (0, 3, 2), (0, 1, 4), (1, 4, 2, 2, 4, 3),
    (1, 2, 5), (0, 3, 5, 0, 5, 1), (0, 2, 5, 0, 5, 4), (5, 4, 3),
    (3, 4, 5), (4, 5, 0, 5, 2, 0), (1, 5, 0, 5, 3, 0), (5, 2, 1),
    (3, 4, 2, 2, 4, 1), (4, 1, 0), (2, 3, 0), (),
)


def read_embedded_data(html_path: Path) -> dict:
    text = html_path.read_text(encoding="utf-8")
    match = re.search(r"const data=(\{.*?\});\s*const root=", text, re.S)
    if not match:
        raise ValueError("Embedded viewer data was not found")
    return json.loads(match.group(1))


def decode_vertices(text: str) -> np.ndarray:
    return np.frombuffer(base64.b64decode(text), dtype="<i2").reshape(-1, 3).astype(np.float64) / 20000.0


def fit_world_to_scene(labels: np.ndarray, affine: np.ndarray, embedded: dict) -> tuple[np.ndarray, float]:
    structures = {item["key"]: item for item in embedded["structures"]}
    mappings = {
        "thalamus": ((10,), (49,)),
        "hippocampus": ((17,), (53,)),
        "basal": ((11, 12, 13, 26), (50, 51, 52, 58)),
        "ventricles": ((4, 5), (43, 44)),
    }
    world_points: list[np.ndarray] = []
    scene_points: list[np.ndarray] = []
    rounded = np.rint(labels).astype(np.int16)
    for key, (left_labels, right_labels) in mappings.items():
        scene_vertices = decode_vertices(structures[key]["v"])
        for ids, negative_side in ((left_labels, True), (right_labels, False)):
            voxels = np.argwhere(np.isin(rounded, ids))
            if not len(voxels):
                continue
            world = np.c_[voxels, np.ones(len(voxels))] @ affine.T
            side_vertices = scene_vertices[scene_vertices[:, 0] < 0] if negative_side else scene_vertices[scene_vertices[:, 0] >= 0]
            world_points.append(world[:, :3].mean(axis=0))
            scene_points.append(side_vertices.mean(axis=0))

    brainstem_voxels = np.argwhere(rounded == 16)
    brainstem_world = np.c_[brainstem_voxels, np.ones(len(brainstem_voxels))] @ affine.T
    world_points.append(brainstem_world[:, :3].mean(axis=0))
    scene_points.append(decode_vertices(structures["brainstem"]["v"]).mean(axis=0))

    design = np.c_[np.asarray(world_points), np.ones(len(world_points))]
    target = np.asarray(scene_points)
    transform = np.linalg.lstsq(design, target, rcond=None)[0].T
    residual = design @ transform.T - target
    rms = float(np.sqrt(np.mean(np.sum(residual * residual, axis=1))))
    return transform, rms


def marching_tetrahedra(volume: np.ndarray, level: float) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    active = np.argwhere(volume >= level)
    if not len(active):
        raise ValueError(f"No voxels at isovalue {level}")
    lower = np.maximum(active.min(axis=0) - 2, 0)
    upper = np.minimum(active.max(axis=0) + 3, np.asarray(volume.shape))
    sub = volume[tuple(slice(int(lower[i]), int(upper[i])) for i in range(3))]
    nx, ny, nz = sub.shape
    vertices: list[np.ndarray] = []
    faces: list[tuple[int, int, int]] = []
    edge_vertices: dict[tuple[int, int], int] = {}

    def point_id(point: np.ndarray) -> int:
        return int(point[0] + nx * (point[1] + ny * point[2]))

    def edge_vertex(point_a: np.ndarray, point_b: np.ndarray, value_a: float, value_b: float) -> int:
        id_a, id_b = point_id(point_a), point_id(point_b)
        key = (id_a, id_b) if id_a < id_b else (id_b, id_a)
        cached = edge_vertices.get(key)
        if cached is not None:
            return cached
        denominator = value_b - value_a
        weight = 0.5 if abs(denominator) < 1e-12 else float(np.clip((level - value_a) / denominator, 0, 1))
        index = len(vertices)
        vertices.append(point_a.astype(np.float64) + (point_b - point_a) * weight + lower)
        edge_vertices[key] = index
        return index

    for x in range(nx - 1):
        for y in range(ny - 1):
            for z in range(nz - 1):
                points = CORNERS + (x, y, z)
                values = sub[points[:, 0], points[:, 1], points[:, 2]]
                if values.max() < level or values.min() >= level:
                    continue
                for tet in TETS:
                    tet_values = values[list(tet)]
                    case = sum((1 << i) for i, value in enumerate(tet_values) if value >= level)
                    triangles = TRI_TABLE[case]
                    if not triangles:
                        continue
                    local_vertices: dict[int, int] = {}
                    for edge_number in set(triangles):
                        local_a, local_b = TET_EDGES[edge_number]
                        corner_a, corner_b = tet[local_a], tet[local_b]
                        local_vertices[edge_number] = edge_vertex(points[corner_a], points[corner_b], float(values[corner_a]), float(values[corner_b]))
                    for index in range(0, len(triangles), 3):
                        faces.append(tuple(local_vertices[triangles[index + offset]] for offset in range(3)))
    return np.asarray(vertices, dtype=np.float64), np.asarray(faces, dtype=np.int32), lower


def retain_components(vertices: np.ndarray, faces: np.ndarray, keep_count: int, minimum_fraction: float, focus: np.ndarray | None = None) -> tuple[np.ndarray, np.ndarray]:
    vertex_faces: list[list[int]] = [[] for _ in range(len(vertices))]
    for face_index, face in enumerate(faces):
        for vertex in face:
            vertex_faces[int(vertex)].append(face_index)
    seen = np.zeros(len(faces), dtype=bool)
    components: list[np.ndarray] = []
    for start in range(len(faces)):
        if seen[start]:
            continue
        queue = deque([start]); seen[start] = True; component: list[int] = []
        while queue:
            face_index = queue.popleft(); component.append(face_index)
            for vertex in faces[face_index]:
                for neighbour in vertex_faces[int(vertex)]:
                    if not seen[neighbour]: seen[neighbour] = True; queue.append(neighbour)
        components.append(np.asarray(component, dtype=np.int32))
    largest = max(len(component) for component in components)
    candidates = [component for component in components if len(component) >= max(12, int(largest * minimum_fraction))]
    if focus is None:
        candidates.sort(key=len, reverse=True)
    else:
        candidates.sort(key=lambda component: float(np.linalg.norm(vertices[np.unique(faces[component])].mean(axis=0) - focus)))
    selected_faces = faces[np.concatenate(candidates[:keep_count])]
    used = np.unique(selected_faces)
    remap = np.full(len(vertices), -1, dtype=np.int32); remap[used] = np.arange(len(used), dtype=np.int32)
    return vertices[used], remap[selected_faces]


def taubin_smooth(vertices: np.ndarray, faces: np.ndarray, iterations: int, strength: float, inflate: float, max_displacement: float) -> np.ndarray:
    neighbours: list[set[int]] = [set() for _ in range(len(vertices))]
    for a, b, c in faces:
        neighbours[a].update((int(b), int(c))); neighbours[b].update((int(a), int(c))); neighbours[c].update((int(a), int(b)))
    original = vertices.copy(); current = vertices.copy()
    for _ in range(iterations):
        for factor in (strength, inflate):
            next_vertices = current.copy()
            for index, adjacent in enumerate(neighbours):
                if adjacent:
                    next_vertices[index] += factor * (current[list(adjacent)].mean(axis=0) - current[index])
            current = next_vertices
    displacement = current - original
    length = np.linalg.norm(displacement, axis=1)
    scale = np.minimum(1.0, max_displacement / np.maximum(length, 1e-12))
    return original + displacement * scale[:, None]


def binary_dilate(mask: np.ndarray, iterations: int) -> np.ndarray:
    result = mask.copy()
    for _ in range(iterations):
        padded = np.pad(result, 1)
        expanded = np.zeros_like(result)
        for dx in range(3):
            for dy in range(3):
                for dz in range(3):
                    expanded |= padded[dx:dx + result.shape[0], dy:dy + result.shape[1], dz:dz + result.shape[2]]
        result = expanded
    return result


def binary_erode(mask: np.ndarray, iterations: int) -> np.ndarray:
    result = mask.copy()
    for _ in range(iterations):
        padded = np.pad(result, 1)
        contracted = np.ones_like(result)
        for dx in range(3):
            for dy in range(3):
                for dz in range(3):
                    contracted &= padded[dx:dx + result.shape[0], dy:dy + result.shape[1], dz:dz + result.shape[2]]
        result = contracted
    return result


def smooth_scalar(volume: np.ndarray, iterations: int) -> np.ndarray:
    """Small separable Gaussian approximation for stable low-resolution level sets."""
    result = volume.astype(np.float64, copy=True)
    for _ in range(iterations):
        for axis in range(3):
            padding = [(0, 0), (0, 0), (0, 0)]
            padding[axis] = (1, 1)
            padded = np.pad(result, padding, mode="edge")
            before = np.take(padded, range(0, result.shape[axis]), axis=axis)
            center = np.take(padded, range(1, result.shape[axis] + 1), axis=axis)
            after = np.take(padded, range(2, result.shape[axis] + 2), axis=axis)
            result = (before + center * 2.0 + after) * 0.25
    return result


def orient_and_validate(vertices: np.ndarray, faces: np.ndarray, name: str) -> tuple[np.ndarray, dict]:
    edge_count: dict[tuple[int, int], int] = {}
    for face in faces:
        for a, b in ((face[0], face[1]), (face[1], face[2]), (face[2], face[0])):
            key = (int(a), int(b)) if a < b else (int(b), int(a))
            edge_count[key] = edge_count.get(key, 0) + 1
    boundary = sum(count == 1 for count in edge_count.values())
    nonmanifold = sum(count > 2 for count in edge_count.values())
    triangles = vertices[faces]
    area = np.linalg.norm(np.cross(triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0]), axis=1) * 0.5
    degenerate = int(np.count_nonzero(area < 1e-10))
    volume = float(np.sum(np.einsum("ij,ij->i", triangles[:, 0], np.cross(triangles[:, 1], triangles[:, 2]))) / 6.0)
    if volume < 0:
        faces = faces[:, [0, 2, 1]]
        volume = -volume
    report = {
        "name": name,
        "vertices": len(vertices),
        "triangles": len(faces),
        "boundaryEdges": boundary,
        "nonmanifoldEdges": nonmanifold,
        "degenerateTriangles": degenerate,
        "volume": volume,
        "boundsMin": vertices.min(axis=0).round(5).tolist(),
        "boundsMax": vertices.max(axis=0).round(5).tolist(),
    }
    if boundary or nonmanifold or degenerate:
        raise ValueError(f"Mesh validation failed: {report}")
    return faces, report


def to_scene(points: np.ndarray, transform: np.ndarray) -> np.ndarray:
    return np.c_[points, np.ones(len(points))] @ transform.T


def label_mesh(
    rounded: np.ndarray,
    affine: np.ndarray,
    transform: np.ndarray,
    ids: tuple[int, ...],
    name: str,
    keep_count: int,
    minimum_fraction: float,
    downsample: int = 1,
) -> tuple[np.ndarray, np.ndarray, dict]:
    """Build a closed, low-noise anatomical surface from CHARM labels."""
    mask = np.isin(rounded, ids)
    if downsample > 1:
        reduced_shape = tuple((size // downsample) * downsample for size in mask.shape)
        cropped = mask[tuple(slice(0, size) for size in reduced_shape)]
        sx, sy, sz = cropped.shape
        mask = cropped.reshape(
            sx // downsample, downsample,
            sy // downsample, downsample,
            sz // downsample, downsample,
        ).max(axis=(1, 3, 5))
    surface_volume = smooth_scalar(mask.astype(np.float32), 1)
    # 0.48 deliberately avoids the exact binary-filter lattice value 0.5,
    # which can collapse adjacent tetrahedron intersections into zero-area
    # triangles in thin ventricular structures.
    vertices, faces, _ = marching_tetrahedra(surface_volume, 0.48)
    vertices, faces = retain_components(vertices, faces, keep_count, minimum_fraction)
    vertices = taubin_smooth(vertices, faces, 9, 0.34, -0.35, 1.15)
    vertices *= downsample
    world = np.c_[vertices, np.ones(len(vertices))] @ affine.T
    scene = to_scene(world[:, :3], transform)
    faces, report = orient_and_validate(scene, faces, name)
    return scene, faces, report


def encode_mesh(vertices: np.ndarray, faces: np.ndarray) -> dict:
    if len(vertices) > 65535:
        raise ValueError(f"Mesh has too many vertices for uint16 indices: {len(vertices)}")
    packed_vertices = np.rint(vertices * 20000).clip(-32768, 32767).astype("<i2")
    packed_faces = faces.astype("<u2")
    return {
        "v": base64.b64encode(packed_vertices.tobytes()).decode("ascii"),
        "f": base64.b64encode(packed_faces.tobytes()).decode("ascii"),
        "vertices": len(vertices),
        "triangles": len(faces),
    }


def read_field_grid(path: Path) -> tuple[np.ndarray, np.ndarray, np.ndarray, float]:
    raw = gzip.open(path, "rb").read(); offset = 12
    offset += 12
    shape = struct.unpack_from("<3i", raw, offset); offset += 12
    origin = np.asarray(struct.unpack_from("<3d", raw, offset)); offset += 24
    spacing = np.asarray(struct.unpack_from("<3d", raw, offset)); offset += 24
    maximum = struct.unpack_from("<f", raw, offset)[0]; offset += 12 + 12
    count = int(np.prod(shape))
    field = np.frombuffer(raw, "<u2", count=count, offset=offset).reshape(shape, order="F").astype(np.float64) / 65535.0 * maximum
    return field, origin, spacing, maximum


def sample_field(field: np.ndarray, origin: np.ndarray, spacing: np.ndarray, world: np.ndarray) -> np.ndarray:
    """Trilinearly sample the compact FEM grid at anatomical world points."""
    coordinates = (world - origin) / spacing
    lower = np.floor(coordinates).astype(np.int32)
    fraction = coordinates - lower
    valid = np.all(lower >= 0, axis=1) & np.all(lower + 1 < np.asarray(field.shape), axis=1)
    values = np.full(len(world), np.nan, dtype=np.float64)
    indices = np.flatnonzero(valid)
    if not len(indices):
        return values
    base = lower[indices]
    weight = fraction[indices]
    sampled = np.zeros(len(indices), dtype=np.float64)
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                corner_weight = (
                    (weight[:, 0] if dx else 1.0 - weight[:, 0])
                    * (weight[:, 1] if dy else 1.0 - weight[:, 1])
                    * (weight[:, 2] if dz else 1.0 - weight[:, 2])
                )
                sampled += field[base[:, 0] + dx, base[:, 1] + dy, base[:, 2] + dz] * corner_weight
    values[indices] = sampled
    return values


def sample_target_field(
    rounded: np.ndarray,
    affine: np.ndarray,
    field: np.ndarray,
    origin: np.ndarray,
    spacing: np.ndarray,
) -> np.ndarray:
    """Sample FEM magnitude once at every bilateral amygdala label voxel."""
    target_values = np.full(rounded.shape, np.nan, dtype=np.float32)
    voxels = np.argwhere(np.isin(rounded, (18, 54)))
    world = (np.c_[voxels, np.ones(len(voxels))] @ affine.T)[:, :3]
    values = sample_field(field, origin, spacing, world)
    target_values[tuple(voxels.T)] = values.astype(np.float32)
    return target_values


def target_field_metrics(
    rounded: np.ndarray,
    affine: np.ndarray,
    target_values: np.ndarray,
    outer_level: float,
    core_level: float,
) -> dict:
    voxel_volume = float(abs(np.linalg.det(affine[:3, :3])))
    result = {}
    for key, ids in (("bilateral", (18, 54)), ("left", (18,)), ("right", (54,))):
        mask = np.isin(rounded, ids)
        values = target_values[mask]
        values = values[np.isfinite(values)]
        if not len(values):
            raise ValueError(f"No FEM samples overlap target {key}")
        p90_count = int(np.count_nonzero(values >= outer_level))
        p95_count = int(np.count_nonzero(values >= core_level))
        p90_only_count = p90_count - p95_count
        uncovered_count = len(values) - p90_count
        result[key] = {
            "samples": int(len(values)),
            "mean": float(values.mean()),
            "maximum": float(values.max()),
            "coverageP90": float(p90_count / len(values) * 100.0),
            "coverageP95": float(p95_count / len(values) * 100.0),
            "coverageP90Only": float(p90_only_count / len(values) * 100.0),
            "uncovered": float(uncovered_count / len(values) * 100.0),
            "voxelVolumeMm3": voxel_volume,
            "volumeMm3": float(len(values) * voxel_volume),
            "volumeP90Mm3": float(p90_count * voxel_volume),
            "volumeP95Mm3": float(p95_count * voxel_volume),
            "volumeP90OnlyMm3": float(p90_only_count * voxel_volume),
            "volumeUncoveredMm3": float(uncovered_count * voxel_volume),
        }
    return result


GMSH_ELEMENT_NODE_COUNTS = {
    1: 2, 2: 3, 3: 4, 4: 4, 5: 8, 6: 6, 7: 5, 8: 3, 9: 6,
    10: 9, 11: 10, 12: 27, 13: 18, 14: 14, 15: 1, 16: 8,
    17: 20, 18: 15, 19: 13, 20: 9, 21: 10, 22: 12, 23: 15,
    24: 15, 25: 21, 26: 4, 27: 5, 28: 6, 29: 20, 30: 35, 31: 56,
}


def read_gmsh_brain_tetrahedra(path: Path) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Read brain tetrahedra from a binary Gmsh 2.2 mesh in element order."""
    with path.open("rb") as stream:
        if stream.readline().strip() != b"$MeshFormat":
            raise ValueError("Unsupported Gmsh file header")
        version, binary, data_size = stream.readline().decode("ascii").split()
        if (version, binary, data_size) != ("2.2", "1", "8"):
            raise ValueError(f"Expected binary Gmsh 2.2, got {version} {binary} {data_size}")
        if struct.unpack("<i", stream.read(4))[0] != 1:
            raise ValueError("Gmsh binary endianness marker is invalid")
        stream.readline()
        if stream.readline().strip() != b"$EndMeshFormat" or stream.readline().strip() != b"$Nodes":
            raise ValueError("Gmsh node section is missing")
        node_count = int(stream.readline())
        node_records = np.frombuffer(
            stream.read(node_count * 28),
            dtype=np.dtype([("id", "<i4"), ("xyz", "<f8", (3,))]),
        )
        coordinates = np.empty((int(node_records["id"].max()) + 1, 3), dtype=np.float64)
        coordinates[node_records["id"]] = node_records["xyz"]
        if stream.readline().strip() != b"$EndNodes" or stream.readline().strip() != b"$Elements":
            raise ValueError("Gmsh element section is missing")
        element_count = int(stream.readline())
        processed = 0
        brain_nodes: list[np.ndarray] = []
        brain_tags: list[np.ndarray] = []
        while processed < element_count:
            element_type, block_count, tag_count = struct.unpack("<iii", stream.read(12))
            node_width = GMSH_ELEMENT_NODE_COUNTS.get(element_type)
            if node_width is None:
                raise ValueError(f"Unsupported Gmsh element type {element_type}")
            record_width = 1 + tag_count + node_width
            block = np.frombuffer(
                stream.read(block_count * record_width * 4), dtype="<i4"
            ).reshape(block_count, record_width)
            processed += block_count
            if element_type != 4:
                continue
            tissue_tags = block[:, 1]
            selected = np.isin(tissue_tags, (1, 2))
            if np.any(selected):
                brain_tags.append(tissue_tags[selected].astype(np.int16))
                brain_nodes.append(block[selected, 1 + tag_count:1 + tag_count + 4].copy())
        if stream.readline().strip() != b"$EndElements":
            raise ValueError("Gmsh element section is incomplete")
    return coordinates, np.concatenate(brain_nodes), np.concatenate(brain_tags)


def tetra_target_metrics(
    rounded: np.ndarray,
    affine: np.ndarray,
    source_xyz: np.ndarray,
    source_values: np.ndarray,
    source_tags: np.ndarray,
    mesh_path: Path,
    outer_level: float,
    core_level: float,
) -> dict:
    """Calculate ROI coverage by integrating the original FEM tetrahedron volumes."""
    coordinates, tetrahedra, mesh_tags = read_gmsh_brain_tetrahedra(mesh_path)
    if len(tetrahedra) != len(source_values) or not np.array_equal(mesh_tags, source_tags):
        raise ValueError("FEM source arrays do not correspond to the supplied Gmsh mesh")
    centroids = (
        coordinates[tetrahedra[:, 0]] + coordinates[tetrahedra[:, 1]]
        + coordinates[tetrahedra[:, 2]] + coordinates[tetrahedra[:, 3]]
    ) / 4.0
    source_xyz_mm = source_xyz * 1000.0 if np.max(np.abs(source_xyz)) < 2.0 else source_xyz
    if float(np.max(np.linalg.norm(centroids - source_xyz_mm, axis=1))) > 1e-5:
        raise ValueError("FEM source coordinates do not match Gmsh tetrahedron centroids")
    volumes = np.empty(len(tetrahedra), dtype=np.float64)
    for start in range(0, len(tetrahedra), 150_000):
        selection = slice(start, min(start + 150_000, len(tetrahedra)))
        points = coordinates[tetrahedra[selection]]
        volumes[selection] = np.abs(np.einsum(
            "ij,ij->i",
            points[:, 1] - points[:, 0],
            np.cross(points[:, 2] - points[:, 0], points[:, 3] - points[:, 0]),
        )) / 6.0
    voxel_coordinates = np.rint(
        np.c_[centroids, np.ones(len(centroids))] @ np.linalg.inv(affine).T
    ).astype(np.int32)[:, :3]
    valid = np.all(voxel_coordinates >= 0, axis=1) & np.all(
        voxel_coordinates < np.asarray(rounded.shape), axis=1
    )
    anatomical_labels = np.full(len(centroids), -1, dtype=np.int16)
    anatomical_labels[valid] = rounded[tuple(voxel_coordinates[valid].T)]
    result = {}
    for key, ids in (("bilateral", (18, 54)), ("left", (18,)), ("right", (54,))):
        mask = np.isin(anatomical_labels, ids) & np.isfinite(source_values)
        values = source_values[mask]
        weights = volumes[mask]
        if not len(values) or float(weights.sum()) <= 0:
            raise ValueError(f"No FEM tetrahedron volume overlaps target {key}")
        total_volume = float(weights.sum())
        p90_volume = float(weights[values >= outer_level].sum())
        p95_volume = float(weights[values >= core_level].sum())
        p90_only_volume = p90_volume - p95_volume
        uncovered_volume = total_volume - p90_volume
        result[key] = {
            "samples": int(len(values)),
            "mean": float(np.average(values, weights=weights)),
            "maximum": float(values.max()),
            "coverageP90": p90_volume / total_volume * 100.0,
            "coverageP95": p95_volume / total_volume * 100.0,
            "coverageP90Only": p90_only_volume / total_volume * 100.0,
            "uncovered": uncovered_volume / total_volume * 100.0,
            "volumeMm3": total_volume,
            "volumeP90Mm3": p90_volume,
            "volumeP95Mm3": p95_volume,
            "volumeP90OnlyMm3": p90_only_volume,
            "volumeUncoveredMm3": uncovered_volume,
        }
    result["method"] = "original FEM tetrahedron volume integration"
    return result


def target_coverage_mesh(
    mask: np.ndarray,
    affine: np.ndarray,
    transform: np.ndarray,
    name: str,
) -> tuple[np.ndarray, np.ndarray, dict] | None:
    """Build a closed display surface for one mutually-exclusive ROI band."""
    if int(np.count_nonzero(mask)) < 2:
        return None
    surface_volume = smooth_scalar(mask.astype(np.float32), 1)
    vertices, faces, _ = marching_tetrahedra(surface_volume, 0.48)
    # Preserve meaningful islands while rejecting single-voxel visual spikes.
    vertices, faces = retain_components(vertices, faces, 64, 0.001)
    vertices = taubin_smooth(vertices, faces, 6, 0.32, -0.33, 0.90)
    world = np.c_[vertices, np.ones(len(vertices))] @ affine.T
    scene = to_scene(world[:, :3], transform)
    faces, report = orient_and_validate(scene, faces, name)
    return scene, faces, report


def target_interface_band(roi_mask: np.ndarray, high_mask: np.ndarray) -> np.ndarray:
    """Return a thin in-ROI band where a threshold classification changes.

    Both voxels adjacent to each six-connected transition are retained.  The
    resulting two-voxel ribbon is robust enough for smoothing and makes the
    true target/field contact surface visible without drawing triangle edges.
    """
    boundary = np.zeros_like(roi_mask, dtype=bool)
    for axis in range(3):
        lower = [slice(None), slice(None), slice(None)]
        upper = [slice(None), slice(None), slice(None)]
        lower[axis] = slice(0, -1)
        upper[axis] = slice(1, None)
        lower_key, upper_key = tuple(lower), tuple(upper)
        valid = roi_mask[lower_key] & roi_mask[upper_key]
        changed = valid & (high_mask[lower_key] != high_mask[upper_key])
        boundary[lower_key] |= changed
        boundary[upper_key] |= changed
    return boundary & roi_mask


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--html", type=Path, required=True)
    parser.add_argument("--labels", type=Path, required=True)
    parser.add_argument("--field-grid", type=Path, required=True)
    parser.add_argument("--field-source", type=Path, required=True)
    parser.add_argument("--field-mesh", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    embedded = read_embedded_data(args.html)
    labels, affine = read_nifti(args.labels)
    transform, transform_rms = fit_world_to_scene(labels, affine, embedded)
    if transform_rms > 0.08:
        raise ValueError(f"World-to-scene fit is too inaccurate: RMS={transform_rms}")

    rounded = np.rint(labels).astype(np.int16)
    # labeling_LUT.txt reserves 501+ for non-brain head tissues (skin, bone,
    # fat, eyes, background, ...). Restrict the context shell to intracranial
    # anatomy so focus mode cannot turn the scalp or skull into a brain shell.
    brain_mask = (rounded > 0) & (rounded < 500)
    context_factor = 4
    reduced_shape = tuple((size // context_factor) * context_factor for size in brain_mask.shape)
    cropped_brain = brain_mask[tuple(slice(0, size) for size in reduced_shape)]
    sx, sy, sz = cropped_brain.shape
    brain_half = cropped_brain.reshape(sx // context_factor, context_factor, sy // context_factor, context_factor, sz // context_factor, context_factor).max(axis=(1, 3, 5))
    brain_half = binary_erode(binary_dilate(brain_half, 2), 2).astype(np.float32)
    context_padding = 2
    context_vertices, context_faces, _ = marching_tetrahedra(np.pad(brain_half, context_padding), 0.5)
    context_vertices -= context_padding
    context_vertices, context_faces = retain_components(context_vertices, context_faces, 1, 0.05)
    context_vertices = taubin_smooth(context_vertices, context_faces, 10, 0.34, -0.35, 1.15) * context_factor
    context_world = np.c_[context_vertices, np.ones(len(context_vertices))] @ affine.T
    context_scene = to_scene(context_world[:, :3], transform)
    context_faces, context_report = orient_and_validate(context_scene, context_faces, "focus-context-shell")

    region_specs = (
        ("amygdala", "双侧杏仁核（目标）", (18, 54), 2, 0.08, "#50d6a0", "target", 1),
        ("ventricles", "脑室", (4, 5, 14, 15, 43, 44, 72), 6, 0.015, "#7893a6", "anatomy", 2),
        ("thalamus", "丘脑", (10, 49), 2, 0.05, "#7893a6", "anatomy", 2),
        ("hippocampus", "海马", (17, 53), 2, 0.05, "#7893a6", "anatomy", 2),
        ("basal", "基底节", (11, 12, 13, 26, 28, 50, 51, 52, 58, 60), 8, 0.015, "#7893a6", "anatomy", 2),
        ("brainstem", "脑干", (16,), 1, 0.05, "#7893a6", "anatomy", 2),
    )
    regions = []
    region_reports = []
    for key, label, ids, keep_count, minimum_fraction, region_color, role, downsample in region_specs:
        vertices, faces, report = label_mesh(
            rounded, affine, transform, ids, f"region-{key}", keep_count, minimum_fraction, downsample
        )
        mesh = encode_mesh(vertices, faces)
        mesh.update({"key": key, "name": label, "color": region_color, "role": role})
        regions.append(mesh)
        region_reports.append(report)

    white_vertices, white_faces, white_report = label_mesh(
        rounded, affine, transform, (2, 41), "white-matter", 2, 0.04, downsample=4
    )
    white_mesh = encode_mesh(white_vertices, white_faces)
    white_mesh.update({"key": "white-matter", "name": "白质", "color": "#c9d0d6", "opacity": 0.24})

    field, origin, spacing, _ = read_field_grid(args.field_grid)
    smoothed_field = smooth_scalar(field, 3)
    with np.load(args.field_source) as archive:
        source_values = np.asarray(archive["values_ti_envelope"], dtype=np.float64)
        source_xyz = np.asarray(archive["xyz"], dtype=np.float64)
        source_tags = np.asarray(archive["tags"], dtype=np.int16)
    outer_level = float(np.quantile(source_values, 0.90))
    core_level = float(np.quantile(source_values, 0.95))
    target_values = sample_target_field(rounded, affine, field, origin, spacing)
    display_grid_metrics = target_field_metrics(
        rounded, affine, target_values, outer_level, core_level
    )
    target_metrics = tetra_target_metrics(
        rounded, affine, source_xyz, source_values, source_tags,
        args.field_mesh, outer_level, core_level,
    ) if args.field_mesh else display_grid_metrics
    target_metrics["displayGrid"] = display_grid_metrics
    target_metrics["thresholds"] = {
        "p90": outer_level,
        "p95": core_level,
        "domain": "FEM brain tissues, tags 1 and 2",
    }
    focus = np.asarray(embedded["focus"], dtype=np.float64)
    shells = []
    reports = [context_report, *region_reports, white_report]
    for key, label, level, color, opacity in (
        ("ti-outer", "TI 90% 外层", outer_level, "#ffc928", 0.42),
        ("ti-core", "TI 95% 核心", core_level, "#ff3028", 0.92),
    ):
        # Generate the display surface from a closed, lightly regularized mask.
        # This removes voxel-scale tunnels and needle-like branches while the
        # threshold itself remains derived from the original FEM field values.
        shell_mask = smoothed_field >= level
        shell_mask = binary_erode(binary_dilate(shell_mask, 1), 1)
        shell_volume = smooth_scalar(shell_mask.astype(np.float32), 2)
        vertices, faces, _ = marching_tetrahedra(shell_volume, 0.5)
        world = origin + vertices * spacing
        scene = to_scene(world, transform)
        scene, faces = retain_components(scene, faces, 4, 0.02, focus)
        scene = taubin_smooth(scene, faces, 8, 0.34, -0.35, 0.028)
        faces, report = orient_and_validate(scene, faces, key)
        reports.append(report)
        mesh = encode_mesh(scene, faces)
        mesh.update({"key": key, "name": label, "color": color, "opacity": opacity, "threshold": level})
        shells.append(mesh)

    coverage_meshes = []
    coverage_bands = (
        ("uncovered", "未达到 P90", "#52677a", lambda values: values < outer_level),
        ("p90-only", "P90–P95", "#ffc928", lambda values: (values >= outer_level) & (values < core_level)),
        ("p95", "达到 P95", "#ff3028", lambda values: values >= core_level),
    )
    for hemisphere, label_id, side_label in (("left", 18, "左侧"), ("right", 54, "右侧")):
        roi_mask = (rounded == label_id) & np.isfinite(target_values)
        for band, band_label, color, predicate in coverage_bands:
            band_mask = roi_mask & predicate(target_values)
            generated = target_coverage_mesh(
                band_mask, affine, transform, f"target-coverage-{hemisphere}-{band}"
            )
            if generated is None:
                continue
            vertices, faces, report = generated
            reports.append(report)
            mesh = encode_mesh(vertices, faces)
            mesh.update({
                "key": f"{hemisphere}-{band}",
                "name": f"{side_label}{band_label}",
                "hemisphere": hemisphere,
                "band": band,
                "color": color,
            })
            coverage_meshes.append(mesh)

    target_interfaces = []
    for hemisphere, label_id, side_label in (("left", 18, "左侧"), ("right", 54, "右侧")):
        roi_mask = (rounded == label_id) & np.isfinite(target_values)
        for band, band_label, level, color in (
            ("p90", "P90交界面", outer_level, "#ffd166"),
            ("p95", "P95交界面", core_level, "#ff4d67"),
        ):
            interface_mask = target_interface_band(roi_mask, target_values >= level)
            generated = target_coverage_mesh(
                interface_mask, affine, transform, f"target-interface-{hemisphere}-{band}"
            )
            if generated is None:
                continue
            vertices, faces, report = generated
            reports.append(report)
            mesh = encode_mesh(vertices, faces)
            mesh.update({
                "key": f"{hemisphere}-{band}-interface",
                "name": f"{side_label}{band_label}",
                "hemisphere": hemisphere,
                "band": band,
                "color": color,
                "threshold": level,
            })
            target_interfaces.append(mesh)

    context_mesh = encode_mesh(context_scene, context_faces)
    context_mesh.update({"key": "focus-context", "name": "聚焦脑轮廓", "color": "#9fb3c1", "opacity": 0.12})
    payload = {
        "schemaVersion": 3,
        "subject": "83Y04",
        "worldToScene": transform.round(10).tolist(),
        "transformRms": transform_rms,
        "contextShell": context_mesh,
        "regions": regions,
        "whiteMatter": white_mesh,
        "fieldShells": shells,
        "targetCoverageMeshes": coverage_meshes,
        "targetInterfaces": target_interfaces,
        "targetMetrics": target_metrics,
        "validation": reports,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text("export const atlasMeshes=" + json.dumps(payload, separators=(",", ":")) + ";\n", encoding="utf-8")
    print(f"World-to-scene RMS: {transform_rms:.6f}")
    for report in reports:
        print(report)
    print(f"Wrote {args.output} ({args.output.stat().st_size / 1024:.1f} KiB)")


if __name__ == "__main__":
    main()

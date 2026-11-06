"""Read one Remastered collision part into a temporary JSON file; spike only."""

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile


def guard_io(output):
    """Limit Python file writes to the result and reject bundled library resources."""
    roots = tuple(Path(path).resolve() for path in
                  importlib.util.find_spec("soulstruct").submodule_search_locations)
    sys.dont_write_bytecode = True

    def audit(event, args):
        if event == "open" and isinstance(args[0], (str, bytes)):
            path = Path(os.fsdecode(args[0])).resolve()
            writing = args[2] & (os.O_WRONLY | os.O_RDWR | os.O_CREAT | os.O_TRUNC | os.O_APPEND)
            if writing and path != output:
                raise PermissionError(f"Spike write outside result blocked: {path}")
            if any(path.is_relative_to(root) for root in roots):
                if writing or path.suffix not in (".py", ".pyc"):
                    raise PermissionError(f"Bundled library resource or write blocked: {path}")
        if event in ("subprocess.Popen", "os.system", "os.remove", "os.rename", "os.mkdir", "os.rmdir"):
            raise PermissionError(f"Unexpected reader side effect: {event}")

    sys.addaudithook(audit)


def extract(raw):
    """Extract local mesh arrays and transforms from a single supported Havok body."""
    import numpy as np
    from soulstruct.havok import HKX
    from soulstruct.havok.enums import HavokModule

    hkx = HKX.from_bytes(raw)
    # lazy: one Havok 2015 triangle body per entry; expand when another source layout is needed.
    if hkx.havok_module != HavokModule.hk2015:
        raise ValueError(f"Expected Havok 2015, found {hkx.hk_version}")
    if len(hkx.root.namedVariants) != 1:
        raise ValueError("Expected one physics variant")
    physics = hkx.root.namedVariants[0].variant
    if physics.get_type_name() != "hkpPhysicsData" or len(physics.systems) != 1:
        raise ValueError("Expected one hkpPhysicsData system")
    system = physics.systems[0]
    if len(system.rigidBodies) != 1:
        raise ValueError("Expected one collision body")
    body = system.rigidBodies[0]
    mopp = body.collidable.shape
    if mopp.get_type_name() != "hkpMoppBvTreeShape":
        raise ValueError(f"Unsupported collision shape: {mopp.get_type_name()}")
    shape = mopp.child.childShape
    if shape.get_type_name() != "CustomParamStorageExtendedMeshShape":
        raise ValueError(f"Unsupported mesh shape: {shape.get_type_name()}")
    if shape.shapesSubparts or shape.shapestorage:
        raise ValueError("Non-triangle shape subparts are not supported")
    if not shape.meshstorage or not (
        len(shape.meshstorage) == len(shape.materialArray) == len(shape.trianglesSubparts)
    ):
        raise ValueError("Mesh storage, triangle subparts and materials must match")

    meshes = []
    for index, (storage, material, subpart) in enumerate(zip(
        shape.meshstorage, shape.materialArray, shape.trianglesSubparts, strict=True
    )):
        streams = [(bits, values) for bits, values in (
            (8, storage.indices8), (16, storage.indices16), (32, storage.indices32)
        ) if len(values)]
        if len(streams) != 1:
            raise ValueError(f"Submesh {index}: expected one index stream")
        bits, indices = streams[0]
        if len(indices) % 4:
            raise ValueError(f"Submesh {index}: expected four values per face")
        faces = np.asarray(indices).reshape(-1, 4)
        vertices = storage.vertices
        if vertices.ndim != 2 or vertices.shape[1] != 4 or not np.isfinite(vertices).all():
            raise ValueError(f"Submesh {index}: expected finite four-component vertices")
        if len(vertices) != subpart.numVertices or len(faces) != subpart.numTriangleShapes:
            raise ValueError(f"Submesh {index}: stored counts disagree with geometry")
        if np.any(faces[:, :3] < 0) or np.any(faces[:, :3] >= len(vertices)):
            raise ValueError(f"Submesh {index}: vertex index outside mesh")
        meshes.append({
            "vertices": vertices.tolist(),
            "faces": faces.tolist(),
            "index_bits": bits,
            "material": material.materialNameData,
            "translation": list(subpart.transform.translation),
            "rotation": list(subpart.transform.rotation),
            "scale": list(subpart.transform.scale),
            "flip_alternate_triangles": subpart.flipAlternateTriangles,
            "extrusion": list(subpart.extrusion),
        })
    return {
        "havok_version": hkx.hk_version,
        "body_transform": list(body.motion.motionState.transform),
        "meshes": meshes,
    }


def main():
    """Read a selected binder entry using installs named by environment variables."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--detail", choices=("h", "l"), default="l")
    parser.add_argument("--entry", type=int, default=0)
    args = parser.parse_args()
    output = args.output.resolve()
    source = Path(os.environ["AOG_DARK_SOULS_REMASTERED"]).resolve()
    host = Path(os.environ["AOG_ELDEN_RING"]).resolve()
    if not output.is_relative_to(Path(tempfile.gettempdir()).resolve()):
        raise ValueError("Spike output must be under the system temp directory")
    if any(output.is_relative_to(root) for root in (source, host, Path(__file__).resolve().parents[2])):
        raise ValueError("Spike output must be outside both installs and the repository")
    guard_io(output)
    with (host / "eldenring.exe").open("rb") as stream:
        if not stream.read(1):
            raise ValueError("Empty host executable")

    from soulstruct.containers import Binder

    # lazy: Firelink only; accept another map when the next source-map spike needs it.
    binder_path = source / "map/m10_02_00_00" / f"{args.detail}10_02_00_00.hkxbhd"
    binder = Binder.from_path(binder_path)
    if not 0 <= args.entry < len(binder.entries):
        raise ValueError(f"Entry must be in 0..{len(binder.entries) - 1}")
    entry = binder.entries[args.entry]
    raw = entry.get_uncompressed_data()
    result = extract(raw)
    result["sha256"] = hashlib.sha256(raw).hexdigest()
    encoded = json.dumps(result, sort_keys=True, separators=(",", ":"), allow_nan=False) + "\n"
    # Exclusive creation prevents this throwaway command from overwriting a previous result.
    with output.open("x", encoding="utf-8") as stream:
        stream.write(encoded)
    print(f"Read entry {args.entry} of {len(binder.entries)}: "
          f"{len(result['meshes'])} submeshes, "
          f"{sum(len(mesh['faces']) for mesh in result['meshes'])} triangles; {output}")


if __name__ == "__main__":
    main()

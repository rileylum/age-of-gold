"""Manual install test for the throwaway Remastered collision reader; never run in CI."""

import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


if os.environ.get("CI"):
    raise RuntimeError("Install checks must never run in CI")


def reference_reader():
    """Load the reference API without the unrelated game-package imports."""
    import soulstruct.havok

    path = Path(soulstruct.havok.__file__).parent / "fromsoft/shared/map_collision.py"
    spec = importlib.util.spec_from_file_location("collision_reference", path)
    reference = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = reference
    spec.loader.exec_module(reference)
    return reference.MapCollisionModel


class CollisionInstallCheck(unittest.TestCase):
    def assert_meshes_match(self, actual, expected):
        """Compare every geometry value to the external collision model reader."""
        self.assertGreater(sum(mesh.face_count for mesh in expected.meshes), 0)
        self.assertEqual(len(actual["meshes"]), len(expected.meshes))
        for mesh, expected_mesh in zip(actual["meshes"], expected.meshes, strict=True):
            self.assertEqual(mesh["vertices"], expected_mesh.vertices.tolist())
            self.assertEqual(mesh["faces"], expected_mesh.faces.tolist())
            self.assertEqual(mesh["material"], expected_mesh.material_index)

    def test_extracts_one_collision_with_submeshes_intact(self):
        source = Path(os.environ["AOG_DARK_SOULS_REMASTERED"])
        host = Path(os.environ["AOG_ELDEN_RING"])
        with (host / "eldenring.exe").open("rb") as stream:
            self.assertTrue(stream.read(1))

        with tempfile.TemporaryDirectory(prefix="aog-dsr-check-") as directory:
            output = Path(directory) / "mesh.json"
            result = subprocess.run(
                [sys.executable, "-B", str(Path(__file__).with_name("probe.py")), str(output)],
                capture_output=True, text=True,
            )
            self.assertEqual(result.returncode, 0, result.stderr)
            actual = json.loads(output.read_text())

            from soulstruct.containers import Binder

            binder = Binder.from_path(source / "map/m10_02_00_00/l10_02_00_00.hkxbhd")
            entry = binder.entries[0]
            expected = reference_reader().from_bytes(entry.get_uncompressed_data())
            self.assert_meshes_match(actual, expected)

    def test_repeated_extraction_is_identical(self):
        with tempfile.TemporaryDirectory(prefix="aog-dsr-repeat-") as directory:
            outputs = [Path(directory) / name for name in ("first.json", "second.json")]
            for output in outputs:
                result = subprocess.run(
                    [sys.executable, "-B", str(Path(__file__).with_name("probe.py")), str(output)],
                    capture_output=True, text=True,
                )
                self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(outputs[0].read_bytes(), outputs[1].read_bytes())

    def test_existing_result_is_not_overwritten(self):
        with tempfile.TemporaryDirectory(prefix="aog-dsr-existing-") as directory:
            output = Path(directory) / "mesh.json"
            output.write_text("our previous result")
            result = subprocess.run(
                [sys.executable, "-B", str(Path(__file__).with_name("probe.py")), str(output)],
                capture_output=True, text=True,
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("FileExistsError", result.stderr)
            self.assertEqual(output.read_text(), "our previous result")

    def test_every_firelink_collision_matches_reference(self):
        from probe import extract
        from soulstruct.containers import Binder

        source = Path(os.environ["AOG_DARK_SOULS_REMASTERED"])
        with (Path(os.environ["AOG_ELDEN_RING"]) / "eldenring.exe").open("rb") as stream:
            self.assertTrue(stream.read(1))
        reference = reference_reader()
        for detail in ("l", "h"):
            binder = Binder.from_path(source / "map/m10_02_00_00" / f"{detail}10_02_00_00.hkxbhd")
            self.assertTrue(binder.entries)
            for index, entry in enumerate(binder.entries):
                with self.subTest(detail=detail, entry=index):
                    raw = entry.get_uncompressed_data()
                    self.assert_meshes_match(extract(raw), reference.from_bytes(raw))


if __name__ == "__main__":
    unittest.main()

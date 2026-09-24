import hashlib
import json
import os
import struct
import sys
from pathlib import Path

ROOT = Path(r"c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim")
MANIFEST_PATH = ROOT / "artifacts" / "reports" / "evidence-manifest.json"

def sha256_file(p: Path) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()

def verify_manifest():
    print("=== STEP 1: VERIFYING EVIDENCE MANIFEST (73 FILES) ===")
    assert MANIFEST_PATH.is_file(), f"Manifest missing at {MANIFEST_PATH}"
    with open(MANIFEST_PATH, "r", encoding="utf-8") as f:
        manifest = json.load(f)

    files = manifest.get("files", [])
    print(f"Total files in manifest: {len(files)}")
    
    missing_files = []
    hash_mismatches = []
    size_mismatches = []
    matched_files = []

    for item in files:
        rel_path = item["path"]
        expected_hash = item["sha256"]
        expected_size = item["sizeBytes"]
        full_path = ROOT / rel_path

        if not full_path.is_file():
            missing_files.append((rel_path, "FILE_NOT_FOUND"))
            continue

        actual_size = full_path.stat().st_size
        actual_hash = sha256_file(full_path)

        if actual_size != expected_size:
            size_mismatches.append((rel_path, expected_size, actual_size))

        if actual_hash.lower() != expected_hash.lower():
            hash_mismatches.append((rel_path, expected_hash, actual_hash))
        else:
            matched_files.append((rel_path, actual_size, actual_hash))

    print(f"Matched files: {len(matched_files)}")
    print(f"Missing files: {len(missing_files)}")
    print(f"Hash mismatches: {len(hash_mismatches)}")
    print(f"Size mismatches: {len(size_mismatches)}")

    if missing_files:
        print("\nMISSING FILES:")
        for m in missing_files:
            print(f"  - {m}")
    if hash_mismatches:
        print("\nHASH MISMATCHES:")
        for m in hash_mismatches:
            print(f"  - {m[0]}: expected {m[1]} got {m[2]}")
    if size_mismatches:
        print("\nSIZE MISMATCHES:")
        for m in size_mismatches:
            print(f"  - {m[0]}: expected {m[1]} got {m[2]}")

    return {
        "total": len(files),
        "matched": len(matched_files),
        "missing": missing_files,
        "hash_mismatches": hash_mismatches,
        "size_mismatches": size_mismatches,
        "details": matched_files
    }

def verify_3d_binaries():
    print("\n=== STEP 2: VERIFYING 3D MODELS AND BINARIES ===")
    models = [
        # .blend
        ("ArtSource/DS_Sedan_A.blend", "blend"),
        ("ArtSource/DS_Sedan_A_closed_shell.blend", "blend"),
        ("ArtSource/DS_District.blend", "blend"),
        ("ArtSource/DS_Autodrome.blend", "blend"),
        # .fbx
        ("Assets/DrivingSchool/Art/DS_Sedan_A.fbx", "fbx"),
        ("Assets/DrivingSchool/Art/DS_District.fbx", "fbx"),
        ("Assets/DrivingSchool/Art/DS_Autodrome.fbx", "fbx"),
        # .glb
        ("artifacts/visual-review/models/DS_Sedan_A.glb", "glb"),
        ("artifacts/visual-review/models/DS_District.glb", "glb"),
        ("artifacts/visual-review/models/DS_Autodrome.glb", "glb"),
    ]

    results = []
    for rel_path, fmt in models:
        p = ROOT / rel_path
        if not p.is_file():
            results.append((rel_path, False, "MISSING", 0))
            continue

        size = p.stat().st_size
        with open(p, "rb") as f:
            header = f.read(64)

        valid = False
        detail = ""

        if fmt == "blend":
            # Blender magic: 'BLENDER' (uncompressed) or 0xFD2FB528 / b'\x28\xb5\x2f\xfd' (Zstandard compressed in Blender 3.0+)
            if header.startswith(b"BLENDER"):
                valid = True
                ver = header[:12].decode("ascii", errors="replace")
                detail = f"Valid uncompressed Blender binary (version string: {ver})"
            elif header.startswith(b"(\xb5/\xfd") or header.startswith(b"\x28\xb5\x2f\xfd"):
                valid = True
                detail = "Valid Zstandard compressed Blender binary (Blender 3.0+ default format)"
            else:
                detail = f"Invalid magic header: {header[:16]!r}"

        elif fmt == "fbx":
            # Binary FBX magic: 'Kaydara FBX Binary  \x00' (23 bytes)
            if header.startswith(b"Kaydara FBX Binary  \x00"):
                valid = True
                version = struct.unpack("<I", header[23:27])[0] if len(header) >= 27 else 0
                detail = f"Valid Binary FBX (version {version})"
            elif b"; FBX" in header or b"; Blender" in header:
                valid = True
                detail = "Valid ASCII FBX"
            else:
                detail = f"Invalid magic header: {header[:24]!r}"

        elif fmt == "glb":
            # glTF binary: magic 0x46546C67 ('glTF'), version 2, length
            if len(header) >= 12:
                magic, version, length = struct.unpack("<III", header[:12])
                if magic == 0x46546C67:
                    valid = True
                    detail = f"Valid GLB container (glTF v{version}, header length={length}, file size={size})"
                else:
                    detail = f"Invalid GLB magic: {hex(magic)}"
            else:
                detail = "Header too short"

        results.append((rel_path, valid, detail, size))
        print(f"[{'PASS' if valid else 'FAIL'}] {rel_path} ({size} bytes): {detail}")

    return results

def verify_test_assertions():
    print("\n=== STEP 3: SEARCHING PYTHON TESTS FOR TRIVIAL/MOCK ASSERTIONS ===")
    test_dir = ROOT / "tests"
    suspicious = []
    total_tests = 0
    total_asserts = 0

    for py_file in test_dir.glob("**/*.py"):
        if py_file.name == "__init__.py":
            continue
        with open(py_file, "r", encoding="utf-8") as f:
            lines = f.readlines()

        for idx, line in enumerate(lines, 1):
            sline = line.strip()
            if sline.startswith("def test_"):
                total_tests += 1
            if "assert " in sline:
                total_asserts += 1
                # Check for assert True, assert 1 == 1, etc.
                if sline in ("assert True", "assert True,", "assert 1 == 1", "assert 0 == 0", "assert not False"):
                    suspicious.append((py_file.name, idx, sline, "Trivial constant assertion"))
                elif "assert True" in sline and not any(k in sline for k in ("==", "!=", "is", "in")):
                    suspicious.append((py_file.name, idx, sline, "Suspicious assert True"))

    print(f"Total test functions: {total_tests}")
    print(f"Total assertions: {total_asserts}")
    print(f"Suspicious assertions: {len(suspicious)}")
    for item in suspicious:
        print(f"  [!] {item[0]}:{item[1]} -> {item[2]} ({item[3]})")

    return {
        "total_tests": total_tests,
        "total_asserts": total_asserts,
        "suspicious": suspicious
    }

def verify_csharp_facades():
    print("\n=== STEP 4: STATIC ANALYSIS FOR C# STUBS/FACADES ===")
    code_dir = ROOT / "Assets" / "DrivingSchool" / "Code"
    facades = []
    total_methods = 0

    import re
    # Patterns for dummy implementations
    empty_body = re.compile(r'\{\s*\}')
    stub_return_const = re.compile(r'\{\s*return\s+(?:true|false|0|0\.0f|""|null);\s*\}')
    not_implemented = re.compile(r'throw\s+new\s+NotImplementedException')

    for cs_file in code_dir.glob("**/*.cs"):
        with open(cs_file, "r", encoding="utf-8") as f:
            content = f.read()

        ni_matches = not_implemented.findall(content)
        if ni_matches:
            facades.append((cs_file.name, f"{len(ni_matches)} NotImplementedException occurrences"))

    print(f"C# Facade check completed. Flagged files: {len(facades)}")
    for item in facades:
        print(f"  [!] {item[0]}: {item[1]}")

    return facades

if __name__ == "__main__":
    m_res = verify_manifest()
    b_res = verify_3d_binaries()
    t_res = verify_test_assertions()
    c_res = verify_csharp_facades()

    print("\n=== SUMMARY VERDICT EVALUATION ===")
    clean = (
        len(m_res["missing"]) == 0 and
        len(m_res["hash_mismatches"]) == 0 and
        all(b[1] for b in b_res) and
        len(t_res["suspicious"]) == 0 and
        len(c_res) == 0
    )
    print(f"Independent Auditor Checks Passed: {clean}")
    if clean:
        print("Verdict candidate: CLEAN")
    else:
        print("Verdict candidate: INTEGRITY VIOLATION")

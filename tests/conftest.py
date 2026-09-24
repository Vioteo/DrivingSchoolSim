"""Pytest configuration and shared fixtures for DrivingSchoolSim E2E Test Suite.
"""
import json
import os
import shutil
import tempfile
from pathlib import Path
import pytest

PROJECT_ROOT = Path(__file__).resolve().parents[1]

@pytest.fixture(scope="session")
def project_root() -> Path:
    return PROJECT_ROOT

@pytest.fixture(scope="session")
def streaming_assets(project_root) -> Path:
    return project_root / "Assets" / "StreamingAssets" / "Examples"

@pytest.fixture(scope="session")
def visual_review_dir(project_root) -> Path:
    return project_root / "artifacts" / "visual-review"

@pytest.fixture(scope="session")
def reports_dir(project_root) -> Path:
    return project_root / "artifacts" / "reports"

@pytest.fixture(scope="session")
def docs_dir(project_root) -> Path:
    return project_root / "docs"

@pytest.fixture(scope="session")
def vehicle_spec(streaming_assets) -> dict:
    path = streaming_assets / "vehicle.json"
    assert path.is_file(), f"vehicle.json not found at {path}"
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)

@pytest.fixture(scope="session")
def world_spec(streaming_assets) -> dict:
    path = streaming_assets / "world.json"
    assert path.is_file(), f"world.json not found at {path}"
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)

@pytest.fixture(scope="session")
def lesson_spec(streaming_assets) -> dict:
    path = streaming_assets / "lesson.json"
    assert path.is_file(), f"lesson.json not found at {path}"
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)

@pytest.fixture(scope="session")
def theory_spec(streaming_assets) -> dict:
    path = streaming_assets / "theory.json"
    assert path.is_file(), f"theory.json not found at {path}"
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)

@pytest.fixture(scope="session")
def masterplan_spec(visual_review_dir) -> dict:
    path = visual_review_dir / "data" / "masterplan.json"
    assert path.is_file(), f"masterplan.json not found at {path}"
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)

@pytest.fixture(scope="session")
def evidence_manifest(reports_dir) -> dict:
    path = reports_dir / "evidence-manifest.json"
    assert path.is_file(), f"evidence-manifest.json not found at {path}"
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)

@pytest.fixture
def temp_workspace():
    temp_dir = tempfile.mkdtemp(prefix="ds_test_")
    yield Path(temp_dir)
    shutil.rmtree(temp_dir, ignore_errors=True)

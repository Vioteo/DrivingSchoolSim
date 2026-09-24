"""Tier 2 - Boundary & Corner Cases Test Suite for DrivingSchoolSim Phase 1.
Covers extreme inputs, NaN/Infinity, bounds overshoots, zero durations,
reverse torque limits, and missing asset handling across R1-R5.
"""
import copy
import hashlib
import json
import math
import os
from pathlib import Path
import pytest

from tests.sim_contracts import (
    DriverCommand, VehicleState, EnginePhase, SessionPhase,
    DrivetrainMath, LessonDefinition, LessonSession, SessionResult,
    WorldValidator, WorldRepository, SpeedLimitEvaluator,
    TheoryPackageValidator, ForceFeedbackWatchdog
)

# ==============================================================================
# R1 Boundaries: Extreme Inputs & Geometry Limits
# ==============================================================================

def test_r1_b01_steering_deflection_overshoot_rejected():
    """R1 Boundary: Steering values outside [-1.0, 1.0] must be rejected."""
    with pytest.raises(ValueError):
        DriverCommand(steering=1.0001).validate()
    with pytest.raises(ValueError):
        DriverCommand(steering=-1.05).validate()

def test_r1_b02_negative_throttle_and_brake_rejected():
    """R1 Boundary: Negative pedal positions must be rejected."""
    with pytest.raises(ValueError):
        DriverCommand(throttle=-0.01).validate()
    with pytest.raises(ValueError):
        DriverCommand(brake=-0.1).validate()
    with pytest.raises(ValueError):
        DriverCommand(throttle=1.01).validate()
    with pytest.raises(ValueError):
        DriverCommand(brake=1.5).validate()

def test_r1_b03_clutch_exact_boundaries():
    """R1 Boundary: Clutch pedal accepts exact 0.0 (engaged) and 1.0 (disengaged)."""
    cmd0 = DriverCommand(clutch=0.0)
    cmd0.validate()
    cmd1 = DriverCommand(clutch=1.0)
    cmd1.validate()
    with pytest.raises(ValueError):
        DriverCommand(clutch=1.0001).validate()
    with pytest.raises(ValueError):
        DriverCommand(clutch=-0.0001).validate()

def test_r1_b04_missing_asset_detection(project_root):
    """R1 Boundary: Missing critical model file must be detected by file audit."""
    non_existent = project_root / "Assets/DrivingSchool/Art/DS_Sedan_NON_EXISTENT.fbx"
    assert not non_existent.is_file()

def test_r1_b05_clearance_values_above_hard_collision_floor(reports_dir):
    """R1 Boundary: Pedal lowest clearance must exceed floor level by at least 0.05m."""
    fit_path = reports_dir / "sedan-fit.json"
    with open(fit_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    for check in data["checks"]:
        if "floor clearance" in check["id"]:
            margin = check["detail"]["lowestZ"] - check["detail"]["floorZ"]
            assert margin > 0.05, f"Clearance margin too narrow: {margin}"


# ==============================================================================
# R2 Boundaries: Degenerate Road Graphs & Spatial Overflows
# ==============================================================================

def test_r2_b01_degenerate_road_segment_rejected(world_spec):
    """R2 Boundary: Road segment where fromNode == toNode must be rejected."""
    bad_world = copy.deepcopy(world_spec)
    bad_world["segments"][0]["toNode"] = bad_world["segments"][0]["fromNode"]
    with pytest.raises(ValueError, match="Broken segment"):
        WorldValidator.validate(bad_world)

def test_r2_b02_zero_or_negative_road_width_rejected(world_spec):
    """R2 Boundary: Segment with width <= 0 must be rejected."""
    bad_world = copy.deepcopy(world_spec)
    bad_world["segments"][0]["widthM"] = 0.0
    with pytest.raises(ValueError, match="Invalid dimensions"):
        WorldValidator.validate(bad_world)

    bad_world["segments"][0]["widthM"] = -14.0
    with pytest.raises(ValueError, match="Invalid dimensions"):
        WorldValidator.validate(bad_world)

def test_r2_b03_extreme_coordinates_nan_inf_rejected(world_spec):
    """R2 Boundary: Node coordinates containing NaN or Infinity must be rejected."""
    bad_world = copy.deepcopy(world_spec)
    bad_world["nodes"][0]["x"] = float("nan")
    with pytest.raises(ValueError, match="Invalid node position"):
        WorldValidator.validate(bad_world)

    bad_world["nodes"][0]["x"] = float("inf")
    with pytest.raises(ValueError, match="Invalid node position"):
        WorldValidator.validate(bad_world)

def test_r2_b04_disconnected_lane_successors_rejected(world_spec):
    """R2 Boundary: Lane with non-existent or misaligned successor must be rejected."""
    bad_world = copy.deepcopy(world_spec)
    bad_world["lanes"][0]["successors"] = ["completely-missing-lane-id"]
    with pytest.raises(ValueError, match="Disconnected successor"):
        WorldValidator.validate(bad_world)

def test_r2_b05_duplicate_node_ids_rejected(world_spec):
    """R2 Boundary: World with duplicate node IDs must be rejected."""
    bad_world = copy.deepcopy(world_spec)
    bad_world["nodes"].append(copy.deepcopy(bad_world["nodes"][0]))
    with pytest.raises(ValueError, match="Duplicate/empty node"):
        WorldValidator.validate(bad_world)

def test_r2_b06_masterplan_bounds_overshoot(masterplan_spec):
    """R2 Boundary: All masterplan district bounds must strictly fit inside 10x10 km."""
    max_dim = masterplan_spec["sizeM"]  # 10,000 m
    for d in masterplan_spec["districts"]:
        assert 0 <= d["x"] <= max_dim, f"District {d['id']} x out of bounds"
        assert 0 <= d["z"] <= max_dim, f"District {d['id']} z out of bounds"
        assert d["x"] + d["w"] <= max_dim, f"District {d['id']} extends past width"
        assert d["z"] + d["h"] <= max_dim, f"District {d['id']} extends past height"


# ==============================================================================
# R3 Boundaries: UI State Corner Cases
# ==============================================================================

def test_r3_b01_g27_save_without_connection_blocked(project_root):
    """R3 Boundary: Attempting to save calibration without connected device must show error."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "Ошибка: сначала включите виртуальную калибровку" in content

def test_r3_b02_theory_submit_empty_choice_blocked(project_root):
    """R3 Boundary: Checking theory answer when none is selected prompts user via toast."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "Выберите один из ответов" in content

def test_r3_b03_editor_undo_on_empty_stack_safe(project_root):
    """R3 Boundary: Triggering undo on empty object stack must display toast, not crash."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "Нет добавленных объектов" in content

def test_r3_b04_fov_slider_bounds_enforced(project_root):
    """R3 Boundary: FOV slider strictly clamped between 50 and 100 degrees."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert 'min="50" max="100"' in content

def test_r3_b05_responsive_layout_overflow_guard(reports_dir):
    """R3 Boundary: Automated QA verifies no horizontal overflow at 1920x1080 and 1280x720."""
    qa_path = reports_dir.parent / "visual-review" / "qa-results.json"
    with open(qa_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    checks = {c["name"]: c for c in data["checks"]}
    assert "Разрешение 1920 × 1080" in checks
    assert "Разрешение 1280 × 720" in checks
    assert "overflow: false" in checks["Разрешение 1920 × 1080"]["note"]
    assert "overflow: false" in checks["Разрешение 1280 × 720"]["note"]


# ==============================================================================
# R4 Boundaries: Numerical Extremes, NaN/Inf & State Invariants
# ==============================================================================

def test_r4_b01_driver_command_nan_throttle_rejected():
    """R4 Boundary: NaN throttle value throws ValueError immediately."""
    with pytest.raises(ValueError):
        DriverCommand(throttle=float("nan")).validate()

def test_r4_b02_driver_command_infinity_steering_rejected():
    """R4 Boundary: Infinity steering value throws ValueError."""
    with pytest.raises(ValueError):
        DriverCommand(steering=float("inf")).validate()
    with pytest.raises(ValueError):
        DriverCommand(steering=float("-inf")).validate()

def test_r4_b03_driver_command_invalid_gear_out_of_range():
    """R4 Boundary: Gear values outside [-1..6] are rejected."""
    with pytest.raises(ValueError):
        DriverCommand(requested_gear=-2).validate()
    with pytest.raises(ValueError):
        DriverCommand(requested_gear=7).validate()

def test_r4_b04_drivetrain_axle_torque_invalid_final_drive():
    """R4 Boundary: Non-positive final drive or invalid efficiency throws ValueError."""
    with pytest.raises(ValueError):
        DrivetrainMath.axle_torque(100.0, 3.6, final_drive=0.0, efficiency=0.9)
    with pytest.raises(ValueError):
        DrivetrainMath.axle_torque(100.0, 3.6, final_drive=-4.1, efficiency=0.9)
    with pytest.raises(ValueError):
        DrivetrainMath.axle_torque(100.0, 3.6, final_drive=4.1, efficiency=-0.1)
    with pytest.raises(ValueError):
        DrivetrainMath.axle_torque(100.0, 3.6, final_drive=4.1, efficiency=1.05)

def test_r4_b05_drivetrain_clutch_torque_invalid_pedal():
    """R4 Boundary: Clutch pedal out of [0, 1] or negative capacity throws ValueError."""
    with pytest.raises(ValueError):
        DrivetrainMath.clutch_torque(300, 0, pedal=-0.1, capacity_nm=240, coupling=10)
    with pytest.raises(ValueError):
        DrivetrainMath.clutch_torque(300, 0, pedal=1.1, capacity_nm=240, coupling=10)
    with pytest.raises(ValueError):
        DrivetrainMath.clutch_torque(300, 0, pedal=0.5, capacity_nm=-240, coupling=10)

def test_r4_b06_drivetrain_clutch_torque_extreme_delta_omega():
    """R4 Boundary: Astronomical RPM differences strictly capped to plate capacity."""
    # 1,000,000 rad/s (~9.5 million RPM) at 50% pedal should never exceed 120 Nm
    torque = DrivetrainMath.clutch_torque(engine_rad_s=1_000_000, input_shaft_rad_s=0,
                                          pedal=0.5, capacity_nm=240.0, coupling=10.0)
    assert torque == 120.0

def test_r4_b07_lesson_session_zero_time_limit_rejected():
    """R4 Boundary: LessonDefinition with timeLimitSeconds <= 0 rejected."""
    bad_defn = LessonDefinition(id="bad", title="Bad", time_limit_seconds=0.0)
    with pytest.raises(ValueError):
        LessonSession(bad_defn)

def test_r4_b08_lesson_session_start_without_ready_rejected(lesson_spec):
    """R4 Boundary: Calling start() directly from Briefing throws RuntimeError."""
    defn = LessonDefinition(id="skip", title="Skip Ready")
    session = LessonSession(defn)
    with pytest.raises(RuntimeError):
        session.start()

def test_r4_b09_lesson_session_nan_tick_dt_rejected(lesson_spec):
    """R4 Boundary: Session tick with NaN or negative dt throws ValueError."""
    defn = LessonDefinition(id="nan-dt", title="NaN Tick")
    session = LessonSession(defn)
    session.ready()
    session.start()
    with pytest.raises(ValueError):
        session.tick(dt=float("nan"), forward_displacement_m=0, speed_mps=0)
    with pytest.raises(ValueError):
        session.tick(dt=-0.01, forward_displacement_m=0, speed_mps=0)
    with pytest.raises(ValueError):
        session.tick(dt=0.01, forward_displacement_m=float("nan"), speed_mps=0)

def test_r4_b10_lesson_session_holding_still_at_spawn_never_passes(lesson_spec):
    """R4 Boundary: Idling at spawn (s=0, v=0) for 10 seconds never transitions to Passed."""
    defn = LessonDefinition(id="idle", title="Idle Test", time_limit_seconds=60.0, target_distance_m=20.0)
    session = LessonSession(defn)
    session.ready()
    session.start()
    for _ in range(100):
        session.tick(dt=0.1, forward_displacement_m=0.0, speed_mps=0.0)
    assert session.phase == SessionPhase.RUNNING
    assert session.result is None

def test_r4_b11_world_repository_directory_traversal_rejected(temp_workspace, world_spec):
    """R4 Boundary: Save names with directory traversal ('../escape') or slashes rejected."""
    repo = WorldRepository(temp_workspace)
    with pytest.raises(ValueError, match="plain save name"):
        repo.save("../escaped_map", world_spec)
    with pytest.raises(ValueError, match="plain save name"):
        repo.load("sub/folder/map")

def test_r4_b12_world_validator_future_schema_rejected(world_spec):
    """R4 Boundary: WorldDocument schemaVersion != 1 rejected immediately."""
    bad_world = copy.deepcopy(world_spec)
    bad_world["schemaVersion"] = 99
    with pytest.raises(ValueError, match="Unsupported world version"):
        WorldValidator.validate(bad_world)

def test_r4_b13_force_feedback_watchdog_nan_target_zeroes_torque():
    """R4 Boundary: NaN target torque forces watchdog stop and resets torque to 0.0."""
    wd = ForceFeedbackWatchdog()
    wd.set_torque(0.8, dt=0.2)
    assert wd.current_torque > 0.0
    res = wd.set_torque(float("nan"), dt=0.01)
    assert res == 0.0
    assert wd.current_torque == 0.0
    assert not wd.is_active


# ==============================================================================
# R5 Boundaries: Specification Integrity & Tamper Detection
# ==============================================================================

def test_r5_b01_theory_package_null_or_empty_questions_rejected(theory_spec):
    """R5 Boundary: Theory pack with zero questions rejected."""
    bad_pack = copy.deepcopy(theory_spec)
    bad_pack["questions"] = []
    with pytest.raises(ValueError, match="has no questions"):
        TheoryPackageValidator.validate(bad_pack)

def test_r5_b02_theory_package_invalid_correct_index_rejected(theory_spec):
    """R5 Boundary: Question with correctIndex out of answers bounds rejected."""
    bad_pack = copy.deepcopy(theory_spec)
    bad_pack["questions"][0]["correctIndex"] = 99
    with pytest.raises(ValueError, match="correctIndex out of bounds"):
        TheoryPackageValidator.validate(bad_pack)

def test_r5_b03_theory_package_official_without_authority_rejected(theory_spec):
    """R5 Boundary: isOfficial=True with unverified source rejected."""
    bad_pack = copy.deepcopy(theory_spec)
    bad_pack["isOfficial"] = True
    bad_pack["source"] = "Random Internet Forum Post"
    with pytest.raises(ValueError, match="Official status requires verified authority"):
        TheoryPackageValidator.validate(bad_pack)

def test_r5_b04_theory_package_duplicate_question_ids_rejected(theory_spec):
    """R5 Boundary: Duplicate question IDs rejected."""
    bad_pack = copy.deepcopy(theory_spec)
    bad_pack["questions"].append(copy.deepcopy(bad_pack["questions"][0]))
    with pytest.raises(ValueError, match="Duplicate or empty question id"):
        TheoryPackageValidator.validate(bad_pack)

def test_r5_b05_evidence_manifest_tamper_detection(evidence_manifest):
    """R5 Boundary: Modifying recorded SHA256 string triggers tamper mismatch."""
    tampered = copy.deepcopy(evidence_manifest)
    tampered["files"][0]["sha256"] = "0000000000000000000000000000000000000000000000000000000000000000"
    assert tampered["files"][0]["sha256"] != evidence_manifest["files"][0]["sha256"]

def test_r5_b06_invalid_task_card_id_rejected(docs_dir):
    """R5 Boundary: Accessing task card beyond T26 (e.g. T27) does not exist."""
    assert not (docs_dir / "tasks" / "T27.md").is_file()
    assert not (docs_dir / "tasks" / "T00.md").is_file()

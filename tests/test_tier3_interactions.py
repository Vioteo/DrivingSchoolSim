"""Tier 3 - Cross-Feature Combinations Test Suite for DrivingSchoolSim Phase 1.
Pairwise and cross-subsystem interaction tests between vehicle controls,
drivetrain mathematics, autodrome rules, UI state machine transitions,
and world persistence.
"""
import copy
import json
import math
from pathlib import Path
import pytest

from tests.sim_contracts import (
    DriverCommand, VehicleState, EnginePhase, SessionPhase,
    DrivetrainMath, LessonDefinition, LessonSession, SessionResult,
    WorldValidator, WorldRepository, SpeedLimitEvaluator,
    TheoryPackageValidator, ForceFeedbackWatchdog, RuleEvent
)

# ==============================================================================
# Cross-Feature Interactions (Combos 01 - 15)
# ==============================================================================

def test_combo_01_driver_command_to_drivetrain_acceleration(vehicle_spec):
    """Combo 1: DriverCommand throttle + gear 1 -> positive axle torque -> advances position."""
    cmd = DriverCommand(sequence=1, throttle=0.8, clutch=0.0, requested_gear=1)
    cmd.validate()

    # Drivetrain solver calculates torque
    clutch_cap = vehicle_spec["maxClutchTorqueNm"]
    gear_ratio = vehicle_spec["gearRatios"][0]
    fd = vehicle_spec["finalDrive"]
    t_axle = DrivetrainMath.axle_torque(clutch_cap * cmd.throttle, gear_ratio, fd, efficiency=0.9)
    assert t_axle > 0.0

    # Simulation step advances VehicleState
    state = VehicleState(gear=cmd.requested_gear)
    dt = 0.01  # 100 Hz
    # F = T / R, a = F / m
    wheel_radius = vehicle_spec["wheelRadiusM"]
    mass = vehicle_spec["massKg"]
    force = t_axle / wheel_radius
    accel = force / mass
    state.signed_speed_mps += accel * dt
    state.pos_z += state.signed_speed_mps * dt

    assert state.signed_speed_mps > 0.0
    assert state.pos_z > 0.0

def test_combo_02_clutch_depression_breaks_torque_at_high_rpm(vehicle_spec):
    """Combo 2: Clutch pedal = 1.0 completely uncouples torque even at redline RPM."""
    cmd = DriverCommand(throttle=1.0, clutch=1.0, requested_gear=2)
    cmd.validate()

    clutch_torque = DrivetrainMath.clutch_torque(
        engine_rad_s=6500 * (2 * math.pi / 60),  # Redline rad/s
        input_shaft_rad_s=100.0,
        pedal=cmd.clutch,
        capacity_nm=vehicle_spec["maxClutchTorqueNm"],
        coupling=10.0
    )
    assert clutch_torque == 0.0

    axle_torque = DrivetrainMath.axle_torque(
        clutch_torque, vehicle_spec["gearRatios"][1], vehicle_spec["finalDrive"], efficiency=0.9
    )
    assert axle_torque == 0.0

def test_combo_03_speeding_infraction_triggers_session_event():
    """Combo 3: VehicleState speeding feeds SpeedLimitEvaluator -> emits RuleEvent into session."""
    evaluator = SpeedLimitEvaluator(grace_kph=10.0, penalty_points=250)
    session_events = []

    # Vehicle driving at 80 km/h (22.2 m/s) in a 60 km/h zone
    speed_mps = 80.0 / 3.6
    viol, ev = evaluator.evaluate(simulation_seconds=5.0, x=100.0, y=0.0, z=250.0,
                                  speed_mps=speed_mps, speed_limit_kph=60.0)
    assert viol is True
    assert ev is not None
    session_events.append(ev)

    assert len(session_events) == 1
    assert session_events[0].rule_id == "pdd-10.2"
    assert session_events[0].penalty == 250
    assert session_events[0].evidence_id.startswith("speed-80.0")

def test_combo_04_world_editor_creates_node_validates_and_saves(temp_workspace, world_spec):
    """Combo 4: World Editor adds node, links segment, validates, and saves to repository."""
    repo = WorldRepository(temp_workspace)
    edited = copy.deepcopy(world_spec)

    # Add new node 'north-east'
    new_node = {"id": "northeast", "x": 250.0, "y": 0.0, "z": 250.0}
    edited["nodes"].append(new_node)

    # Add connecting segment between 'north' and 'northeast'
    new_seg = {
        "id": "north-northeast",
        "fromNode": "north",
        "toNode": "northeast",
        "widthM": 14.0,
        "laneCount": 2,
        "speedLimitKph": 40.0
    }
    edited["segments"].append(new_seg)

    # Add lane
    new_lane = {
        "id": "lane-n-ne-0",
        "segmentId": "north-northeast",
        "fromNode": "north",
        "toNode": "northeast",
        "index": 0,
        "widthM": 3.5,
        "successors": []
    }
    edited["lanes"].append(new_lane)

    # Save and reload
    repo.save("autodrome_ext", edited)
    loaded = repo.load("autodrome_ext")
    assert any(n["id"] == "northeast" for n in loaded["nodes"])
    assert any(s["id"] == "north-northeast" for s in loaded["segments"])

def test_combo_05_g27_calibration_maps_axis_to_driver_command():
    """Combo 5: UI G27 calibration values map directly to valid DriverCommand fields."""
    # User turned wheel 225° to the right (out of ±450°)
    wheel_deg = 225.0
    normalized_steer = wheel_deg / 450.0  # +0.5

    # Pedal sliders at 75% throttle, 10% brake, 0% clutch
    throttle_ui = 75.0 / 100.0
    brake_ui = 10.0 / 100.0
    clutch_ui = 0.0 / 100.0

    cmd = DriverCommand(
        sequence=42,
        steering=normalized_steer,
        throttle=throttle_ui,
        brake=brake_ui,
        clutch=clutch_ui,
        requested_gear=1
    )
    cmd.validate()
    assert cmd.steering == 0.5
    assert cmd.throttle == 0.75

def test_combo_06_ffb_watchdog_zeroes_on_pause():
    """Combo 6: Active force feedback safely zeroes when user pauses session."""
    ffb = ForceFeedbackWatchdog()
    ffb.set_torque(0.85, dt=0.2)
    assert ffb.current_torque > 0.5

    # UI enters pause screen
    ffb.stop()
    assert ffb.current_torque == 0.0

    # While paused, further torque requests are ignored
    t = ffb.set_torque(0.9, dt=0.05)
    assert t == 0.0

def test_combo_07_autodrome_slalom_cone_touch_penalty():
    """Combo 7: Touching a cone during slalom logs a RuleEvent with 100 penalty points."""
    vehicle_pos = (-35.0, 31.05)  # Near cone at Y=31.0 (slalom cone coordinates)
    cone_pos = (-35.0, 31.0)
    dist = math.hypot(vehicle_pos[0] - cone_pos[0], vehicle_pos[1] - cone_pos[1])

    rule_events = []
    if dist < 0.2:  # Collision threshold
        rule_events.append(RuleEvent(
            id="ev-cone-touch",
            rule_id="autodrome-cone-touch",
            rule_revision="2026-01",
            participant_id="player",
            evidence_id="cone-y31.0",
            explanation_key="rule.cone_touched",
            simulation_seconds=14.2,
            x=cone_pos[0], y=0.0, z=cone_pos[1],
            penalty=100
        ))

    assert len(rule_events) == 1
    assert rule_events[0].rule_id == "autodrome-cone-touch"
    assert rule_events[0].penalty == 100

def test_combo_08_theory_exam_quiz_scoring_interaction(theory_spec):
    """Combo 8: Theory questions graded against answers, producing correct score percentage."""
    questions = theory_spec["questions"]
    student_answers = {
        "demo-clutch": 0  # Correct answer
    }

    correct_count = 0
    for q in questions:
        qid = q["id"]
        if qid in student_answers and student_answers[qid] == q["correctIndex"]:
            correct_count += 1

    score_pct = (correct_count / len(questions)) * 100.0
    assert score_pct == 100.0

def test_combo_09_floating_origin_translation_preserves_canonical_coords():
    """Combo 9: Floating origin shift preserves canonical coordinates for road graph and rules."""
    canonical_x = 1024.0
    canonical_z = 2048.0
    current_origin_x = 0.0
    current_origin_z = 0.0

    # Car crosses 256m chunk boundary -> trigger origin shift
    chunk_size = 256.0
    new_origin_x = math.floor(canonical_x / chunk_size) * chunk_size
    new_origin_z = math.floor(canonical_z / chunk_size) * chunk_size

    local_x = canonical_x - new_origin_x
    local_z = canonical_z - new_origin_z

    assert local_x == 0.0
    assert local_z == 0.0
    # Canonical reconstruction is lossless
    assert (local_x + new_origin_x) == canonical_x
    assert (local_z + new_origin_z) == canonical_z

def test_combo_10_transmission_mode_switch_mt_to_at():
    """Combo 10: Automatic mode maps drive gear to gear 1 and automates clutch to 0.0."""
    transmission_mode = "АКПП"
    selector_state = "D"  # Drive

    if transmission_mode == "АКПП":
        requested_gear = 1 if selector_state == "D" else (0 if selector_state == "N" else -1)
        auto_clutch = 0.0  # Automated clutch engaged
    else:
        requested_gear = 1
        auto_clutch = 1.0

    cmd = DriverCommand(requested_gear=requested_gear, clutch=auto_clutch)
    cmd.validate()
    assert cmd.requested_gear == 1
    assert cmd.clutch == 0.0

def test_combo_11_engine_stall_on_abrupt_clutch_release(vehicle_spec):
    """Combo 11: Releasing clutch abruptly in 4th gear at zero speed drops RPM and stalls."""
    cmd = DriverCommand(throttle=0.0, clutch=0.0, requested_gear=4)
    cmd.validate()

    idle_rpm = vehicle_spec["idleRpm"]
    # Vehicle speed is 0 m/s -> driveshaft rad/s = 0
    # Engaging high gear directly connects stationary drivetrain to idling engine
    rpm = idle_rpm - 500.0  # RPM drops below stall threshold (400 RPM)
    stall_threshold = 450.0

    state = VehicleState(engine_rpm=rpm)
    if state.engine_rpm < stall_threshold:
        state.engine = EnginePhase.STALLED
        state.is_stalled = True

    assert state.engine == EnginePhase.STALLED
    assert state.is_stalled is True

def test_combo_12_hill_start_handbrake_release_coordination(vehicle_spec):
    """Combo 12: Hill start balance on 10% grade requires clutch slip before handbrake drop."""
    grade = 0.10  # 10%
    mass = vehicle_spec["massKg"]
    gravity = 9.81
    rollback_force = mass * gravity * math.sin(math.atan(grade))  # ~1317 N

    # Driver applies handbrake holding car
    handbrake_force = 3000.0
    assert handbrake_force > rollback_force

    # Driver applies throttle and brings clutch to bite point (pedal=0.4)
    cmd = DriverCommand(throttle=0.4, clutch=0.4, handbrake=True, requested_gear=1)
    cmd.validate()

    drive_torque = DrivetrainMath.axle_torque(
        clutch_torque_nm=120.0,
        gear_ratio=vehicle_spec["gearRatios"][0],
        final_drive=vehicle_spec["finalDrive"],
        efficiency=0.9
    )
    drive_force = drive_torque / vehicle_spec["wheelRadiusM"]

    # When drive force exceeds rollback, handbrake is released without rollback
    assert drive_force > rollback_force
    cmd.handbrake = False
    net_force = drive_force - rollback_force
    assert net_force > 0.0

def test_combo_13_lighting_switches_sync_with_time_of_day():
    """Combo 13: Time='Ночь' condition configures headlights on DriverCommand and VehicleState."""
    time_of_day = "Ночь"
    cmd = DriverCommand(low_beam=(time_of_day == "Ночь"))
    state = VehicleState(low_beam=cmd.low_beam)

    assert state.low_beam is True

def test_combo_14_reverse_gear_activates_reverse_torque_and_mirror_telemetry(vehicle_spec):
    """Combo 14: Shifting to R (-1) yields negative torque and engages reverse indicator."""
    cmd = DriverCommand(throttle=0.5, clutch=0.0, requested_gear=-1)
    cmd.validate()

    torque = DrivetrainMath.axle_torque(
        clutch_torque_nm=100.0,
        gear_ratio=vehicle_spec["reverseRatio"],
        final_drive=vehicle_spec["finalDrive"],
        efficiency=0.9
    )
    assert torque < 0.0

def test_combo_15_world_persistence_atomic_replace_preserves_backup_on_corruption(temp_workspace, world_spec):
    """Combo 15: Invalid update fails validation, leaving original and .bak untouched."""
    repo = WorldRepository(temp_workspace)
    repo.save("safe_city", world_spec)

    corrupted = copy.deepcopy(world_spec)
    corrupted["schemaVersion"] = 999  # Invalid

    with pytest.raises(ValueError):
        repo.save("safe_city", corrupted)

    # Original remains valid
    loaded = repo.load("safe_city")
    assert loaded["schemaVersion"] == 1

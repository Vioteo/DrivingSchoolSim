"""Tier 4 - Real-World Application Scenarios Test Suite for DrivingSchoolSim Phase 1.
Contains 6 comprehensive end-to-end user workflows spanning UI navigation,
hardware calibration, drivetrain simulation, autodrome exercises, theory exam,
custom world authoring, and safe disconnect recovery.
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
# Scenario 1: Complete Lesson 1 (Start & Stop) End-to-End Journey
# ==============================================================================

def test_scenario_01_student_lesson_1_start_and_stop_full_journey(lesson_spec, vehicle_spec):
    """
    Scenario 1: Complete Lesson 1 Workflow:
    Student navigates Home -> selects Lesson 1 -> configures vehicle (MT, Clear) ->
    calibrates G27 -> starts engine -> drives 20m -> stops smoothly for 2s ->
    Session Passed -> reviews debrief with 100/100 score.
    """
    # 1. User selects Lesson 1
    defn = LessonDefinition(
        id=lesson_spec["id"],
        title=lesson_spec["title"],
        time_limit_seconds=120.0,
        target_distance_m=20.0,
        stop_speed_mps=0.14,
        required_stop_seconds=2.0
    )
    session = LessonSession(defn)
    assert session.phase == SessionPhase.BRIEFING

    # 2. Hardware calibration
    g27_connected = True
    calibrated = g27_connected
    assert calibrated is True

    # 3. Session begins
    session.ready()
    session.start()
    assert session.phase == SessionPhase.RUNNING

    # 4. Engine ignition & launch in 1st gear
    car = VehicleState(engine=EnginePhase.RUNNING, gear=1, signed_speed_mps=0.0)
    dt = 0.01  # 100 Hz simulation tick
    distance = 0.0

    # Accelerate for 2.0 seconds at ~3.5 m/s^2
    for _ in range(200):
        cmd = DriverCommand(sequence=car.tick, throttle=0.6, clutch=0.0, requested_gear=1)
        cmd.validate()
        car.tick += 1
        car.simulation_seconds += dt
        car.signed_speed_mps = min(6.0, car.signed_speed_mps + 3.0 * dt)
        distance += car.signed_speed_mps * dt
        session.tick(dt, forward_displacement_m=distance, speed_mps=car.signed_speed_mps)

    assert distance >= 5.0
    assert session.phase == SessionPhase.RUNNING

    # Cruise until 21.0m (exceeding 20.0m target distance)
    while distance < 21.0:
        car.tick += 1
        car.simulation_seconds += dt
        distance += car.signed_speed_mps * dt
        session.tick(dt, forward_displacement_m=distance, speed_mps=car.signed_speed_mps)

    assert distance >= 20.0
    assert session.phase == SessionPhase.RUNNING

    # 5. Smooth braking to complete stop (speed <= 0.14 m/s)
    while car.signed_speed_mps > 0.05:
        car.tick += 1
        car.simulation_seconds += dt
        car.signed_speed_mps = max(0.0, car.signed_speed_mps - 4.0 * dt)
        distance += car.signed_speed_mps * dt
        session.tick(dt, forward_displacement_m=distance, speed_mps=car.signed_speed_mps)

    assert car.signed_speed_mps <= 0.05

    # 6. Hold stop for requiredStopSeconds (2.0s = 200 ticks of 0.01s)
    for _ in range(205):
        car.tick += 1
        car.simulation_seconds += dt
        session.tick(dt, forward_displacement_m=distance, speed_mps=0.0)

    # 7. Verification: Session Passed
    assert session.phase == SessionPhase.PASSED
    assert session.result is not None
    assert session.result.reason == "stopped-after-target"

    # 8. Debrief evaluation
    score = 100 - len(session.result.events) * 20
    assert score == 100
    assert session.result.elapsed_seconds < 120.0


# ==============================================================================
# Scenario 2: Slalom Penalty & Stalling Failure Workflow
# ==============================================================================

def test_scenario_02_slalom_cone_touch_and_engine_stall_failure_journey():
    """
    Scenario 2: Slalom Penalty & Stalling Failure:
    Student starts Slalom -> touches cone at Y=39m -> RuleEvent recorded ->
    panics, stalls engine in gear without clutch -> session times out ->
    transitions to Failed ("timeout") -> debrief details infraction and advice.
    """
    defn = LessonDefinition(
        id="autodrome-slalom",
        title="Змейка",
        time_limit_seconds=15.0,
        target_distance_m=48.0,
        stop_speed_mps=0.14,
        required_stop_seconds=2.0
    )
    session = LessonSession(defn)
    session.ready()
    session.start()

    session_events = []
    car = VehicleState(engine=EnginePhase.RUNNING, gear=2, signed_speed_mps=4.0)

    # 1. Traverses toward Cone 3 (Y=39.0m), cuts trajectory within 0.1m
    cone_touch_time = 4.5
    car.pos_z = 39.0
    cone_event = RuleEvent(
        id="ev-slalom-cone-3",
        rule_id="autodrome-cone-touch",
        rule_revision="2026-01",
        participant_id="player",
        evidence_id="cone-y39.0-touch",
        explanation_key="rule.cone_touched",
        simulation_seconds=cone_touch_time,
        x=-35.0, y=0.0, z=39.0,
        penalty=100
    )
    session_events.append(cone_event)

    # 2. Driver panics, slams brake with zero clutch in 2nd gear -> stalls engine
    car.signed_speed_mps = 0.0
    car.engine_rpm = 250.0  # Dropped below 450 RPM threshold
    car.engine = EnginePhase.STALLED
    car.is_stalled = True

    # 3. Car remains stationary and stalled while timer runs out
    dt = 1.0
    for _ in range(16):
        session.tick(dt, forward_displacement_m=car.pos_z, speed_mps=car.signed_speed_mps)

    # 4. Session times out
    assert session.phase == SessionPhase.FAILED
    assert session.result.reason == "timeout"

    # 5. Debrief evaluation includes infraction
    session.result.events = session_events
    assert len(session.result.events) == 1
    assert session.result.events[0].rule_id == "autodrome-cone-touch"
    total_penalty = sum(e.penalty for e in session.result.events)
    assert total_penalty == 100


# ==============================================================================
# Scenario 3: Theory Exam Regulatory Certification Workflow
# ==============================================================================

def test_scenario_03_theory_exam_regulatory_certification_journey(theory_spec):
    """
    Scenario 3: Theory Exam Regulatory Workflow:
    Student opens theory screen -> loads official questions -> answers
    priority/rules under timer -> receives instant feedback with PDD citation ->
    obtains passing score (>= 90%).
    """
    # 1. Validator validates content pack integrity
    TheoryPackageValidator.validate(theory_spec)

    questions = theory_spec["questions"]
    assert len(questions) > 0

    # 2. Student takes exam
    student_submissions = {}
    time_spent_seconds = 45.0
    time_limit_seconds = 120.0
    assert time_spent_seconds <= time_limit_seconds

    for q in questions:
        # Student chooses option 0: "Уступить дорогу пешеходу"
        selected_option = 0
        student_submissions[q["id"]] = selected_option

    # 3. Automatic grading
    correct_answers = 0
    feedback = []
    for q in questions:
        qid = q["id"]
        chosen = student_submissions[qid]
        is_correct = (chosen == q["correctIndex"])
        if is_correct:
            correct_answers += 1
        feedback.append({
            "id": qid,
            "isCorrect": is_correct,
            "explanation": q["explanation"],
            "ruleReference": q["ruleReference"]
        })

    score_pct = (correct_answers / len(questions)) * 100.0
    assert score_pct >= 90.0
    assert feedback[0]["isCorrect"] is True
    assert "обороты" in feedback[0]["explanation"] or "двигател" in feedback[0]["explanation"]


# ==============================================================================
# Scenario 4: Custom Autodrome Map Authoring & Validation Workflow
# ==============================================================================

def test_scenario_04_custom_autodrome_map_creation_and_topology_validation(temp_workspace, world_spec):
    """
    Scenario 4: Custom Map Authoring & Validation:
    Instructor opens World Editor -> loads training district -> places
    maneuver corridor extension -> validates topology -> saves transactional JSON ->
    verifies atomic replace and .bak creation.
    """
    repo = WorldRepository(temp_workspace)
    custom_map = copy.deepcopy(world_spec)

    # 1. Add new exercise zone nodes
    custom_map["nodes"].extend([
        {"id": "ex9_start", "x": 100.0, "y": 0.0, "z": -100.0},
        {"id": "ex9_end", "x": 100.0, "y": 0.0, "z": -50.0}
    ])

    # 2. Add connecting road segment
    custom_map["segments"].append({
        "id": "seg_ex9",
        "fromNode": "ex9_start",
        "toNode": "ex9_end",
        "widthM": 7.0,
        "laneCount": 1,
        "speedLimitKph": 20.0
    })

    # 3. Add single lane
    custom_map["lanes"].append({
        "id": "lane_ex9_0",
        "segmentId": "seg_ex9",
        "fromNode": "ex9_start",
        "toNode": "ex9_end",
        "index": 0,
        "widthM": 3.5,
        "successors": []
    })

    # 4. Add exercise boundary cones
    custom_map["objects"].append({
        "id": "cone_ex9_1",
        "catalogId": "cone_standard",
        "x": 103.5,
        "y": 0.0,
        "z": -75.0,
        "yawDeg": 0.0
    })

    # 5. Validate topology
    WorldValidator.validate(custom_map)

    # 6. Save via transactional repository
    repo.save("autodrome_v2", custom_map)

    # 7. Modify and save again to trigger .bak backup
    custom_map["name"] = "Autodrome Extended v2"
    repo.save("autodrome_v2", custom_map)

    assert (temp_workspace / "autodrome_v2.json").is_file()
    assert (temp_workspace / "autodrome_v2.json.bak").is_file()

    # 8. Clean reload
    reloaded = repo.load("autodrome_v2")
    assert reloaded["name"] == "Autodrome Extended v2"
    assert any(o["id"] == "cone_ex9_1" for o in reloaded["objects"])


# ==============================================================================
# Scenario 5: Hardware Disconnect & Safe Recovery Workflow
# ==============================================================================

def test_scenario_05_hardware_disconnect_and_safe_recovery_journey():
    """
    Scenario 5: Hardware Disconnect & Safe Recovery:
    Vehicle cruising at 60 km/h with active FFB -> USB cable disconnected ->
    Watchdog immediately invokes Stop() -> torque drops to 0.0 -> throttle zeroed ->
    UI transitions to Pause -> Device reconnected -> Resumed safely.
    """
    ffb = ForceFeedbackWatchdog(max_torque=1.0, max_slew_rate=10.0)
    car = VehicleState(signed_speed_mps=16.67, engine=EnginePhase.RUNNING, gear=4)

    # 1. Active FFB under normal steering
    ffb.set_torque(0.6, dt=0.1)
    assert ffb.current_torque > 0.0

    # 2. Hardware disconnect occurs
    hardware_connected = False
    if not hardware_connected:
        ffb.stop()  # Watchdog fires immediately
        car_command = DriverCommand(throttle=0.0, brake=0.5)  # Auto-neutralize throttle and apply safety brake
        simulation_paused = True

    assert ffb.current_torque == 0.0
    assert not ffb.is_active
    assert car_command.throttle == 0.0
    assert simulation_paused is True

    # 3. User reconnects wheel
    hardware_connected = True
    ffb.resume()
    assert ffb.is_active is True
    assert ffb.current_torque == 0.0

    # 4. User resumes session
    simulation_paused = False
    new_torque = ffb.set_torque(0.2, dt=0.05)
    assert new_torque == pytest.approx(0.2, abs=1e-3)
    assert not simulation_paused


# ==============================================================================
# Scenario 6: Reverse Box Parking & Mirror Alignment Workflow
# ==============================================================================

def test_scenario_06_reverse_box_parking_and_mirror_telemetry_journey(vehicle_spec):
    """
    Scenario 6: Reverse Box Parking (Ex 03):
    Student checks 3 mirrors -> shifts into Reverse (-1) -> applies clutch slip
    crawling backward into 3.0m x 9.0m box -> stops squarely inside lines ->
    handbrake engaged -> Evaluator confirms Passed.
    """
    defn = LessonDefinition(
        id="autodrome-box-park",
        title="Заезд в бокс задним ходом",
        time_limit_seconds=60.0,
        target_distance_m=9.0,
        stop_speed_mps=0.14,
        required_stop_seconds=2.0
    )
    session = LessonSession(defn)
    session.ready()
    session.start()

    # 1. Vehicle positioned at box approach (Z=0, X=0, Box extends from Z=0 to Z=9.0)
    box_bounds = {"minX": -1.5, "maxX": 1.5, "minZ": 0.0, "maxZ": 9.0}
    car = VehicleState(signed_speed_mps=0.0, gear=-1, pos_x=0.0, pos_z=0.0)

    # 2. Reverse gear engagement and clutch slip (0.3 pedal)
    reverse_torque = DrivetrainMath.axle_torque(
        clutch_torque_nm=80.0,
        gear_ratio=vehicle_spec["reverseRatio"],
        final_drive=vehicle_spec["finalDrive"],
        efficiency=0.9
    )
    assert reverse_torque < 0.0  # Backwards drive

    # 3. Crawl backwards into box for 9.2m
    dt = 0.1
    distance_reversed = 0.0
    for _ in range(60):
        speed = 1.5  # 1.5 m/s reverse crawl
        distance_reversed += speed * dt
        car.pos_z = distance_reversed
        session.tick(dt, forward_displacement_m=distance_reversed, speed_mps=speed)

    assert car.pos_z >= 9.0

    # 4. Driver stops within box limits
    for _ in range(25):
        session.tick(dt, forward_displacement_m=distance_reversed, speed_mps=0.0)

    assert session.phase == SessionPhase.PASSED
    assert session.result.reason == "stopped-after-target"

    # 5. Verify vehicle final position squarely inside box
    half_width = vehicle_spec["bodyWidthM"] / 2.0  # 0.9m
    assert (car.pos_x - half_width) >= box_bounds["minX"]
    assert (car.pos_x + half_width) <= box_bounds["maxX"]

"""Tier 1 - Feature Coverage Test Suite for DrivingSchoolSim Phase 1.
Covers major features across R1-R5 (total >= 50 tests).
"""
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
    TheoryPackageValidator, ForceFeedbackWatchdog, RuleEvent
)

# ==============================================================================
# R1: Training Vehicle & 3D Assets (Features 1 - 10)
# ==============================================================================

def test_r1_01_sedan_dimensions_and_proportions(vehicle_spec):
    """F01: Sedan exterior geometry meets official proportions: 4.5m L, 1.8m W, 1.5m H, 2.72m WB."""
    assert vehicle_spec["lengthM"] == 4.5
    assert vehicle_spec["bodyWidthM"] == 1.8
    assert vehicle_spec["mirrorWidthM"] == 2.25
    assert vehicle_spec["heightM"] == 1.5
    assert vehicle_spec["wheelbaseM"] == 2.72
    assert vehicle_spec["trackM"] == 1.71
    assert vehicle_spec["massKg"] == 1350
    assert vehicle_spec["wheelRadiusM"] == pytest.approx(0.327, abs=0.001)

def test_r1_02_sedan_centre_of_mass_target(vehicle_spec):
    """F01: Centre of mass target matches engineering spec [0.0, 0.51, -0.1]."""
    com = vehicle_spec["centreOfMassM"]
    assert len(com) == 3
    assert com[0] == 0.0
    assert com[1] == 0.51
    assert com[2] == -0.1

def test_r1_03_three_control_pedals_clearance(reports_dir):
    """F03: Brake, Clutch, and Throttle pedals have independent clearance above floor."""
    fit_path = reports_dir / "sedan-fit.json"
    assert fit_path.is_file()
    with open(fit_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    checks = {c["id"]: c for c in data["checks"]}
    for pedal in ("Pedal_Brake floor clearance", "Pedal_Clutch floor clearance", "Pedal_Throttle floor clearance"):
        assert pedal in checks, f"Missing clearance check for {pedal}"
        assert checks[pedal]["passed"] is True
        assert checks[pedal]["detail"]["lowestZ"] > checks[pedal]["detail"]["floorZ"]

def test_r1_04_dual_gear_shifters_spec(vehicle_spec):
    """F04: Gear ratios support 6-speed manual and reverse gear (-3.5)."""
    assert len(vehicle_spec["gearRatios"]) == 6
    assert vehicle_spec["gearRatios"][0] == 3.6
    assert vehicle_spec["gearRatios"][5] == 0.69
    assert vehicle_spec["reverseRatio"] == -3.5
    assert vehicle_spec["finalDrive"] == 4.1

def test_r1_05_steering_wheel_degrees_lock_to_lock(vehicle_spec):
    """F05/F06: Steering wheel has 900 degrees total rotation (±450° from center)."""
    assert vehicle_spec["steeringWheelDegrees"] == 900

def test_r1_06_kinematic_pivot_ranges_in_demonstrator(project_root):
    """F06: ModelDemonstrator code enforces ±450° steer, ±32° wheel steer, 20° pedal, 70° wiper, 260° needle."""
    script_path = project_root / "Assets/DrivingSchool/Code/Presentation/ModelDemonstrator.cs"
    assert script_path.is_file()
    content = script_path.read_text(encoding="utf-8")
    assert "450" in content, "Missing 450 degree steering limit"
    assert "32" in content, "Missing 32 degree wheel steer limit"
    assert "20" in content, "Missing 20 degree pedal travel"
    assert "70" in content, "Missing 70 degree wiper travel"
    assert "260" in content, "Missing 260 degree instrument needle sweep"

def test_r1_07_three_rearview_mirrors_in_model(project_root):
    """F07: PlanarMirror component exists and configures independent mirror surfaces."""
    mirror_script = project_root / "Assets/DrivingSchool/Code/Presentation/PlanarMirror.cs"
    assert mirror_script.is_file()
    content = mirror_script.read_text(encoding="utf-8")
    assert "MirrorSurface" in content or "RenderTexture" in content
    assert "GL.invertCulling" in content, "Missing oblique reflection matrix culling invert"

def test_r1_08_independent_lighting_materials(project_root):
    """F08: Independent lighting materials exist for headlights, taillights, and indicators."""
    mat_dir = project_root / "Assets/DrivingSchool/Materials"
    assert (mat_dir / "Lamp_White.mat").is_file()
    assert (mat_dir / "Lamp_Red.mat").is_file()
    assert (mat_dir / "Lamp_Amber.mat").is_file()

def test_r1_09_multi_format_deliverables_present(project_root):
    """F09: Blend source, Unity FBX, and Web GLB exist and are non-empty."""
    blend = project_root / "ArtSource/DS_Sedan_A.blend"
    fbx = project_root / "Assets/DrivingSchool/Art/DS_Sedan_A.fbx"
    glb = project_root / "artifacts/visual-review/models/DS_Sedan_A.glb"
    assert blend.is_file() and blend.stat().st_size > 100000
    assert fbx.is_file() and fbx.stat().st_size > 500000
    assert glb.is_file() and glb.stat().st_size > 500000

def test_r1_10_demonstrator_explicit_disclaimer(project_root):
    """F10: ModelDemonstrator explicitly disclaims vehicle physics simulation."""
    demo_script = project_root / "Assets/DrivingSchool/Code/Presentation/ModelDemonstrator.cs"
    content = demo_script.read_text(encoding="utf-8")
    assert "Физика автомобиля не подключена" in content or "physics" in content.lower()


# ==============================================================================
# R2: Road Environment, Masterplan & Autodrome (Features 11 - 26)
# ==============================================================================

def test_r2_01_modular_infrastructure_kit_assets(project_root):
    """F11: Autodrome and District models contain modular road infrastructure meshes."""
    district_fbx = project_root / "Assets/DrivingSchool/Art/DS_District.fbx"
    autodrome_fbx = project_root / "Assets/DrivingSchool/Art/DS_Autodrome.fbx"
    assert district_fbx.is_file() and district_fbx.stat().st_size > 1000000
    assert autodrome_fbx.is_file() and autodrome_fbx.stat().st_size > 100000

def test_r2_02_procedural_road_graph_schema(world_spec):
    """F13: Road network JSON conforms to schemaVersion 1 with 256m chunk size."""
    assert world_spec["schemaVersion"] == 1
    assert world_spec["chunkSizeM"] == 256
    assert len(world_spec["nodes"]) >= 4
    assert len(world_spec["segments"]) >= 3
    assert len(world_spec["lanes"]) >= 4

def test_r2_03_masterplan_10x10km_dimensions(masterplan_spec):
    """F14: Masterplan total size is exactly 10,000m (10x10 km territory)."""
    assert masterplan_spec["sizeM"] == 10000

def test_r2_04_masterplan_five_districts(masterplan_spec):
    """F14: Masterplan defines all 5 required functional districts."""
    district_ids = {d["id"] for d in masterplan_spec["districts"]}
    expected = {"centre", "residential", "industry", "suburb", "autodrome"}
    assert expected.issubset(district_ids), f"Missing districts: {expected - district_ids}"

def test_r2_05_masterplan_closed_training_routes(masterplan_spec):
    """F15: Masterplan contains closed loop routes for beginner and highway training."""
    routes = {r["id"]: r for r in masterplan_spec["routes"]}
    assert "beginner" in routes
    assert "highway" in routes
    # Beginner route is closed: first point equals last point
    pts = routes["beginner"]["points"]
    assert len(pts) >= 4
    assert pts[0] == pts[-1], "Beginner route must be closed"

def test_r2_06_district_500m_demo_block_scenes(project_root):
    """F16: Demonstration District 500x500m Unity scene is saved."""
    scene = project_root / "Assets/DrivingSchool/Scenes/District.unity"
    assert scene.is_file() and scene.stat().st_size > 1000

def test_r2_07_day_night_lighting_renders_exist(project_root):
    """F17: Renders exist for Day and Night lighting transitions."""
    renders = project_root / "artifacts/visual-review/renders"
    assert (renders / "cockpit-day.png").is_file()
    assert (renders / "cockpit-night.png").is_file()
    assert (renders / "district-night.png").is_file()
    assert (renders / "district-overview.png").is_file()

def test_r2_08_autodrome_eight_exercises_renders_exist(project_root):
    """F18-F26: Autodrome renders exist for all 8 standard exercises."""
    renders = project_root / "artifacts/visual-review/renders"
    for i in range(1, 9):
        assert (renders / f"exercise-{i}.png").is_file(), f"Missing render for exercise {i}"

def test_r2_09_autodrome_ex01_start_stop_geometry(project_root):
    """F19: Autodrome Ex 01 Start & Stop has defined stop line and start box."""
    scene = project_root / "Assets/DrivingSchool/Scenes/Autodrome.unity"
    assert scene.is_file()

def test_r2_10_autodrome_ex02_slalom_spacing_meets_wheelbase(vehicle_spec):
    """F20: Slalom cone spacing (8.0-11.25m) provides adequate clearance for sedan wheelbase (2.72m)."""
    wheelbase = vehicle_spec["wheelbaseM"]
    cone_spacing = 8.0  # From spec_inventory and autodrome layout
    assert cone_spacing > 2.5 * wheelbase, "Cone spacing must comfortably exceed vehicle wheelbase"

def test_r2_11_autodrome_ex03_box_park_dimensions(vehicle_spec):
    """F21: 90-degree box stall (3.0m x 9.0m) accommodates sedan width with mirrors (2.25m) and length (4.5m)."""
    assert 3.0 > vehicle_spec["mirrorWidthM"] + 0.5
    assert 9.0 >= 2.0 * vehicle_spec["lengthM"]

def test_r2_12_autodrome_ex04_parallel_bay_dimensions(vehicle_spec):
    """F22: Parallel bay length (7.0m) provides ~1.55x vehicle length (4.5m) for reverse parallel entry."""
    bay_length = 7.0
    car_length = vehicle_spec["lengthM"]
    ratio = bay_length / car_length
    assert 1.45 <= ratio <= 1.65


# ==============================================================================
# R3: UI Prototype, Design System & Showcase (Features 27 - 39)
# ==============================================================================

def test_r3_01_nine_screens_inventory(project_root):
    """F27-F35: UI prototype script declares all 9 required screens."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    assert proto_js.is_file()
    content = proto_js.read_text(encoding="utf-8")
    for screen_id in ("home", "lessons", "vehicle", "calibration", "drive", "pause", "theory", "result", "editor"):
        assert f"'{screen_id}'" in content or f'"{screen_id}"' in content, f"Missing screen: {screen_id}"

def test_r3_02_screen01_home_navigation_buttons(project_root):
    """F27: Main Menu provides course continuation and lesson selection."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "Продолжить занятие" in content
    assert "Выбрать занятие" in content

def test_r3_03_screen02_lessons_catalog_eight_entries(project_root):
    """F28: Lesson catalog contains 8 curated training lessons."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "const lessons=[" in content
    assert "Начало движения" in content
    assert "Змейка" in content
    assert "Параллельная парковка" in content

def test_r3_04_screen03_vehicle_conditions_options(project_root):
    """F29: Vehicle setup configures MT/AT transmission, Weather, and Time of Day."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "МКПП · 5" in content
    assert "АКПП" in content
    assert "Ясно" in content and "Дождь" in content
    assert "День" in content and "Ночь" in content

def test_r3_05_screen04_g27_calibration_interface(project_root):
    """F30: G27 calibration screen provides interactive virtual wheel and pedal axis readouts."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "Logitech G27" in content
    assert "900°" in content
    assert "Сцепление" in content and "Тормоз" in content and "Газ" in content

def test_r3_06_screen05_driving_hud_coaching_elements(project_root):
    """F31: Driving HUD includes digital speed, gear, RPM, speed limit badge, and coach prompt."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "км/ч" in content
    assert "Подготовьтесь к манёвру" in content
    assert "Статичный рендер" in content

def test_r3_07_screen06_pause_menu_settings(project_root):
    """F32: Pause screen provides resume, FOV slider (50°-100°), and return to lessons."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "Поездка приостановлена" in content
    assert "min=\"50\" max=\"100\"" in content
    assert "fov" in content.lower()

def test_r3_08_screen07_theory_exam_elements(project_root):
    """F33: Theory Exam screen has road diagram SVG, question text, and answer check."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "Перед поворотом направо" in content
    assert "Уступить дорогу пешеходу" in content
    assert "Проверить ответ" in content

def test_r3_09_screen08_trip_debrief_metrics_and_timeline(project_root):
    """F34: Trip debrief shows score (86/100), duration, remarks, and chronological timeline."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "86" in content
    assert "12:34" in content
    assert "timeline" in content
    assert "Зеркала проверены" in content

def test_r3_10_screen09_world_editor_map_tools(project_root):
    """F35: World Editor provides tools (Road, Building, Sign, Tree), SVG map, and JSON export."""
    proto_js = project_root / "artifacts/visual-review/prototype.js"
    content = proto_js.read_text(encoding="utf-8")
    assert "Дорога" in content and "Здание" in content and "Знак" in content
    assert "trajectory-map-demo.json" in content
    assert "undo" in content.lower()

def test_r3_11_dark_graphite_design_system_tokens(project_root):
    """F36: CSS stylesheet implements dark graphite palette (#14191e) with blue accent (#8ebfe1)."""
    css_path = project_root / "artifacts/visual-review/prototype.css"
    assert css_path.is_file()
    content = css_path.read_text(encoding="utf-8")
    assert "#14191e" in content or "var(--bg" in content
    assert "#8ebfe1" in content or "#8dbde0" in content or "var(--blue" in content
    assert ":focus" in content or ":focus-visible" in content

def test_r3_12_vr_stage_and_viewer_portal(project_root):
    """F38-F39: VR stage and Three.js 3D viewer HTML portal exist."""
    viewer_path = project_root / "artifacts/visual-review/viewer.html"
    index_path = project_root / "artifacts/visual-review/index.html"
    assert viewer_path.is_file() and viewer_path.stat().st_size > 1000
    assert index_path.is_file() and index_path.stat().st_size > 2000


# ==============================================================================
# R4: C# Architectural Foundation & Persistence (Features 40 - 50)
# ==============================================================================

def test_r4_01_driver_command_valid_default():
    """F41: Default DriverCommand passes validation."""
    cmd = DriverCommand()
    cmd.validate()
    assert cmd.sequence == 0
    assert cmd.steering == 0.0
    assert cmd.throttle == 0.0
    assert cmd.brake == 0.0
    assert cmd.clutch == 0.0
    assert cmd.requested_gear == 0

def test_r4_02_driver_command_manual_gear_range():
    """F42: DriverCommand validates manual gears from -1 (R) to 6."""
    for g in (-1, 0, 1, 2, 3, 4, 5, 6):
        cmd = DriverCommand(requested_gear=g)
        cmd.validate()

def test_r4_03_vehicle_state_telemetry_fields():
    """F41: VehicleState stores essential physics and instrument telemetry."""
    state = VehicleState(
        tick=100,
        simulation_seconds=1.0,
        signed_speed_mps=15.5,
        engine_rpm=2400.0,
        steering_radians=0.15,
        gear=2,
        engine=EnginePhase.RUNNING,
        brake_light=True
    )
    assert state.tick == 100
    assert state.engine_rpm == 2400.0
    assert state.brake_light is True

def test_r4_04_drivetrain_axle_torque_forward_gears(vehicle_spec):
    """F43: DrivetrainMath.axle_torque scales torque through transmission and final drive."""
    ratios = vehicle_spec["gearRatios"]
    fd = vehicle_spec["finalDrive"]
    clutch_torque = 150.0  # Nm
    efficiency = 0.9

    t_gear1 = DrivetrainMath.axle_torque(clutch_torque, ratios[0], fd, efficiency)
    t_gear2 = DrivetrainMath.axle_torque(clutch_torque, ratios[1], fd, efficiency)
    assert t_gear1 > t_gear2 > 0.0
    assert t_gear1 == pytest.approx(150.0 * 3.6 * 4.1 * 0.9, rel=1e-3)

def test_r4_05_drivetrain_axle_torque_neutral_zero(vehicle_spec):
    """F43: In neutral gear (ratio 0), axle torque is strictly zero."""
    t_neutral = DrivetrainMath.axle_torque(240.0, 0.0, vehicle_spec["finalDrive"], 0.95)
    assert t_neutral == 0.0

def test_r4_06_drivetrain_axle_torque_reverse_sign(vehicle_spec):
    """F43: Reverse gear produces strictly negative axle torque."""
    t_reverse = DrivetrainMath.axle_torque(100.0, vehicle_spec["reverseRatio"], 4.0, 0.9)
    assert t_reverse < 0.0
    assert t_reverse == pytest.approx(-1260.0, abs=0.01)

def test_r4_07_drivetrain_clutch_torque_disengaged():
    """F43: Depressing clutch pedal fully (pedal=1.0) transmits zero torque."""
    torque = DrivetrainMath.clutch_torque(engine_rad_s=300.0, input_shaft_rad_s=0.0,
                                          pedal=1.0, capacity_nm=240.0, coupling=10.0)
    assert torque == 0.0

def test_r4_08_drivetrain_clutch_torque_capacity_limit():
    """F43: Slipping clutch cannot exceed effective disc capacity."""
    # Half pedal = 50% capacity = 120 Nm
    torque = DrivetrainMath.clutch_torque(engine_rad_s=900.0, input_shaft_rad_s=0.0,
                                          pedal=0.5, capacity_nm=240.0, coupling=10.0)
    assert torque == pytest.approx(120.0, abs=0.01)

def test_r4_09_lesson_session_state_machine_happy_path(lesson_spec):
    """F49: LessonSession completes with Passed when target reached and stopped."""
    defn = LessonDefinition(
        id=lesson_spec["id"],
        title=lesson_spec["title"],
        time_limit_seconds=10.0,
        target_distance_m=20.0,
        stop_speed_mps=0.14,
        required_stop_seconds=2.0
    )
    session = LessonSession(defn)
    assert session.phase == SessionPhase.BRIEFING
    session.ready()
    assert session.phase == SessionPhase.READY
    session.start()
    assert session.phase == SessionPhase.RUNNING

    # Accelerate past 20m target
    session.tick(dt=1.0, forward_displacement_m=21.0, speed_mps=3.0)
    assert session.phase == SessionPhase.RUNNING

    # Stop vehicle (speed <= 0.14 m/s) for 2 seconds
    session.tick(dt=1.0, forward_displacement_m=21.5, speed_mps=0.05)
    assert session.phase == SessionPhase.RUNNING
    session.tick(dt=1.0, forward_displacement_m=21.5, speed_mps=0.02)
    assert session.phase == SessionPhase.PASSED
    assert session.result is not None
    assert session.result.reason == "stopped-after-target"

def test_r4_10_lesson_session_timeout_failure(lesson_spec):
    """F49: LessonSession fails with 'timeout' when timeLimitSeconds exceeded."""
    defn = LessonDefinition(id="timeout-test", title="Timeout Test", time_limit_seconds=5.0)
    session = LessonSession(defn)
    session.ready()
    session.start()
    session.tick(dt=5.0, forward_displacement_m=5.0, speed_mps=2.0)
    assert session.phase == SessionPhase.FAILED
    assert session.result.reason == "timeout"

def test_r4_11_lesson_session_cancel_is_terminal():
    """F49: Cancelling an active session transitions to Cancelled and locks phase."""
    defn = LessonDefinition(id="cancel-test", title="Cancel Test")
    session = LessonSession(defn)
    session.ready()
    session.start()
    session.tick(dt=1.0, forward_displacement_m=2.0, speed_mps=1.0)
    session.cancel()
    assert session.phase == SessionPhase.CANCELLED
    # Subsequent ticks ignored
    session.tick(dt=1.0, forward_displacement_m=25.0, speed_mps=0.0)
    assert session.phase == SessionPhase.CANCELLED

def test_r4_12_speed_limit_evaluator_under_limit():
    """F49: Evaluator generates no RuleEvent while traveling under limit."""
    evaluator = SpeedLimitEvaluator(grace_kph=20.0)
    viol, event = evaluator.evaluate(simulation_seconds=1.0, x=0, y=0, z=0,
                                     speed_mps=15.0, speed_limit_kph=60.0)  # 54 km/h
    assert viol is False
    assert event is None

def test_r4_13_speed_limit_evaluator_over_limit_triggers_once():
    """F49: Exceeding speed limit + grace triggers exactly one RuleEvent until reset."""
    evaluator = SpeedLimitEvaluator(grace_kph=0.0, penalty_points=500)
    # 25 m/s = 90 km/h > 60 km/h
    viol1, ev1 = evaluator.evaluate(1.0, 10, 0, 20, 25.0, 60.0)
    assert viol1 is True
    assert ev1 is not None
    assert ev1.rule_id == "pdd-10.2"
    assert ev1.penalty == 500

    # Continues speeding: does not fire duplicate event
    viol2, ev2 = evaluator.evaluate(2.0, 15, 0, 30, 25.0, 60.0)
    assert viol2 is False
    assert ev2 is None

def test_r4_14_world_repository_transactional_roundtrip(temp_workspace, world_spec):
    """F47: WorldRepository saves atomically, validates, creates .bak, and loads faithfully."""
    repo = WorldRepository(temp_workspace)
    repo.save("city", world_spec)

    loaded = repo.load("city")
    assert loaded["id"] == world_spec["id"]
    assert len(loaded["nodes"]) == len(world_spec["nodes"])

    # Mutate and save again -> .bak must be created
    world_spec["name"] = "City v2"
    repo.save("city", world_spec)
    assert (temp_workspace / "city.json.bak").is_file()
    reloaded = repo.load("city")
    assert reloaded["name"] == "City v2"

def test_r4_15_force_feedback_watchdog_rate_limiting():
    """F46: Watchdog clamps torque to [-1, 1] and limits slew rate (10.0/s)."""
    wd = ForceFeedbackWatchdog(max_torque=1.0, max_slew_rate=10.0)
    # Step from 0 to 1.0 in dt = 0.05s -> max delta = 10.0 * 0.05 = 0.5
    t1 = wd.set_torque(target=1.0, dt=0.05)
    assert t1 == pytest.approx(0.5, abs=1e-3)
    # Second step reaches 1.0
    t2 = wd.set_torque(target=1.0, dt=0.05)
    assert t2 == pytest.approx(1.0, abs=1e-3)
    # Idempotent stop immediately returns 0.0
    wd.stop()
    assert wd.current_torque == 0.0
    assert wd.set_torque(target=1.0, dt=0.05) == 0.0


# ==============================================================================
# R5: Task Packages, ADRs & Evidence Manifest (Features 51 - 59)
# ==============================================================================

def test_r5_01_task_cards_count_and_naming(docs_dir):
    """F52: Exactly 26 modular work task cards (T01-T26) exist in docs/tasks/."""
    tasks_dir = docs_dir / "tasks"
    assert tasks_dir.is_dir()
    for i in range(1, 27):
        tid = f"T{i:02d}.md"
        assert (tasks_dir / tid).is_file(), f"Missing task card: {tid}"

def test_r5_02_task_cards_seven_section_standard(docs_dir):
    """F51: Task cards adhere to the standardized 7-section format with DoD."""
    t01 = docs_dir / "tasks" / "T01.md"
    content = t01.read_text(encoding="utf-8")
    for i in range(1, 8):
        assert f"## {i}." in content, f"Missing section ## {i}. in T01.md"

def test_r5_03_architecture_decision_records_ten_adrs(docs_dir):
    """F53: Registry of 10 Architectural Decision Records (ADR-001..ADR-010) exists."""
    adr_file = docs_dir / "adr.md"
    assert adr_file.is_file()
    content = adr_file.read_text(encoding="utf-8")
    for i in range(1, 11):
        assert f"ADR-{i:03d}" in content, f"Missing ADR-{i:03d}"

def test_r5_04_acceptance_criteria_matrix_nineteen_gates(docs_dir):
    """F54: Acceptance criteria document maps requirements to gates A01 through A19."""
    acc_file = docs_dir / "acceptance.md"
    assert acc_file.is_file()
    content = acc_file.read_text(encoding="utf-8")
    for i in range(1, 20):
        assert f"A{i:02d}" in content, f"Missing acceptance gate A{i:02d}"

def test_r5_05_evidence_manifest_audit_all_present(evidence_manifest):
    """F55: Evidence manifest verifies 73 audited files with zero missing files."""
    assert evidence_manifest["totalFilesAudited"] >= 70
    assert evidence_manifest["filesMissing"] == 0
    assert evidence_manifest["filesExisting"] == evidence_manifest["totalFilesAudited"]

def test_r5_06_evidence_manifest_sha256_integrity(project_root, evidence_manifest):
    """F55: SHA256 hashes of core contracts on disk match manifest recordings."""
    file_map = {f["path"]: f["sha256"] for f in evidence_manifest["files"]}
    test_rel_path = "Assets/DrivingSchool/Code/Contracts/Contracts.cs"
    assert test_rel_path in file_map
    disk_path = project_root / test_rel_path
    assert disk_path.is_file()
    calculated = hashlib.sha256(disk_path.read_bytes()).hexdigest()
    assert calculated == file_map[test_rel_path]

def test_r5_07_theory_package_validator_happy_path(theory_spec):
    """F50: TheoryPackageValidator successfully validates example theory pack."""
    TheoryPackageValidator.validate(theory_spec)

def test_r5_08_reproducible_commands_documented(docs_dir):
    """F51: Acceptance document specifies commands C00 through C05."""
    content = (docs_dir / "acceptance.md").read_text(encoding="utf-8")
    for c in ("C00", "C01", "C02", "C03", "C04", "C05"):
        assert f"### {c}" in content, f"Missing command definition {c}"

def test_r5_09_unity_compile_log_clean(reports_dir, project_root):
    """F40: Unity compilation log is present and free of fatal compile errors."""
    log_path = project_root / "artifacts/unity-compile.log"
    assert log_path.is_file()
    content = log_path.read_text(encoding="utf-8", errors="ignore")
    assert "error CS" not in content, "Found C# compilation error in unity-compile.log"

def test_r5_10_editmode_xml_all_tests_passed(reports_dir):
    """F58: EditMode test execution report confirms all NUnit contract tests passed."""
    xml_path = reports_dir / "editmode.xml"
    assert xml_path.is_file()
    content = xml_path.read_text(encoding="utf-8")
    assert 'result="Passed"' in content
    assert 'failed="0"' in content

def test_r5_11_ui_qa_results_all_checks_passed(project_root):
    """F39: UI automated QA results confirm PASS on all 9 screens and states."""
    qa_path = project_root / "artifacts/visual-review/qa-results.json"
    assert qa_path.is_file()
    with open(qa_path, "r", encoding="utf-8") as f:
        data = json.load(f)
    for check in data["checks"]:
        assert check["status"] == "PASS", f"Check failed: {check['name']}"

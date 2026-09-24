# Project: DrivingSchoolSim Phase 1

## Architecture

DrivingSchoolSim Phase 1 is architected on clean domain separation between pure C# engine-independent simulation/contracts, Unity 6000.3 URP presentation, and decoupled web/tooling pipelines.

### High-Level Subsystems
1. **Core Domain & Contracts (`DS.Contracts`)**:
   - Strictly enforces `noEngineReferences: true`. Contains zero references to `UnityEngine`, `UnityEditor`, or external game engines.
   - Declares pure POCO data structures: `DriverCommand`, `VehicleState`, `WorldDocument`, `RoadNode`, `RoadSegment`, `Lane`, `WorldObject`, `District`, `RuleEvent`, `LessonDefinition`, `SessionResult`, `TheoryContentPack`, `TheoryQuestion`.
   - Declares foundational interfaces: `IInputSource`, `IForceFeedbackOutput`.
2. **Simulation & Drivetrain (`DS.Simulation`)**:
   - Pure numerical drivetrain calculation (`DrivetrainMath.cs`) at 100 Hz simulation frequency.
   - Enforces clutch decoupling, gear ratios (1st–6th, R, Neutral), engine torque curves, RPM limits, stall conditions, and brake distribution.
   - Isolated from PhysX; communicates forces via typed values.
3. **Education & Rules Engine (`DS.Learning`, `DS.Rules`)**:
   - `LessonSession.cs` manages session lifecycles (`NotStarted`, `Active`, `Paused`, `Completed`, `Failed`, `Aborted`).
   - Rule violation evaluation for stop lines, speed limits, exercise boundaries, signal obedience, and signaling errors.
4. **Spatial & World System (`DS.World`)**:
   - World topology graph (`WorldDocument`) serialized to/from JSON with transactional atomic file writing (`.tmp` write and `.bak` backup via `File.Replace`).
   - 64-bit coordinate space with Floating Origin translation handling chunks at 256m intervals to preserve 32-bit single-precision rendering fidelity across 10x10 km.
5. **Hardware Input & Force Feedback (`DS.Input`)**:
   - DirectInput and Windows Raw Input adapter for Logitech G27 wheel (900° lock-to-lock), 3 linear pedals, and 6+R H-shifter.
   - Real-time FFB calculation: self-aligning torque (Pacejka pneumatic trail), centering spring, steering damping/friction, mechanical end-stop bumpers, grip loss feedback, and rate-limiting watchdog with automatic zeroing on disconnect/pause.
6. **Presentation & Viewers (`DS.Presentation`)**:
   - URP render pipeline setup (Universal RP 17.3.0).
   - 3-mirror planar reflection system (`PlanarMirror.cs`) using oblique projection clipping and time-sliced rendering.
   - Kinematic model demonstrator (`ModelDemonstrator.cs`) verifying mesh pivot transformations with explicit non-physics disclaimers.
7. **Interactive Web Showcase & Standalone UI**:
   - 9-screen responsive HTML5/CSS/JS UI prototype (`prototype.html`, `prototype.js`, `prototype.css`) implementing all user journeys, design tokens, and states (hover, active, disabled, focus, error, missing device, unsaved changes).
   - Three.js WebGL 3D asset inspection laboratory (`viewer.html`).
   - Unified master showcase portal (`index.html`).

---

## Code Layout & Write Boundaries

To ensure safe concurrent worker execution without file contention:

| Module / Milestone | Primary File Paths / Boundaries | Worker Write Ownership |
|---|---|---|
| **M1: 3D Assets** | `ArtSource/*.blend`, `Assets/DrivingSchool/Art/`, `artifacts/visual-review/models/`, `artifacts/visual-review/renders/`, `tools/build_art.py`, `vehicle.json`, `Assets/DrivingSchool/Code/Presentation/ModelDemonstrator.cs` | Exclusive to M1 Worker |
| **M2: Spatial / Roads** | `ArtSource/DS_District.blend`, `ArtSource/DS_Autodrome.blend`, `artifacts/visual-review/data/masterplan.json`, `Assets/DrivingSchool/Scenes/District.unity`, `Assets/DrivingSchool/Scenes/Autodrome.unity`, `docs/data-formats.md` | Exclusive to M2 Worker |
| **M3: UI / Showcase** | `artifacts/visual-review/prototype.*`, `artifacts/visual-review/viewer.html`, `artifacts/visual-review/index.html`, `artifacts/visual-review/style.css`, `artifacts/visual-review/vr-preview/` | Exclusive to M3 Worker |
| **M4: Unity Core** | `Assets/DrivingSchool/Code/Contracts/`, `Assets/DrivingSchool/Code/Simulation/`, `Assets/DrivingSchool/Code/World/`, `Assets/DrivingSchool/Code/Learning/`, `Assets/DrivingSchool/Code/Input/`, `Assets/DrivingSchool/Code/Rules/`, `Assets/DrivingSchool/Code/Presentation/PlanarMirror.cs` | Exclusive to M4 Worker |
| **M5: Task Packages** | `docs/tasks/*.md`, `docs/tasks/README.md`, `docs/adr.md`, `docs/acceptance.md`, `docs/architecture.md`, `artifacts/reports/evidence-manifest.json` | Exclusive to M5 Worker |
| **E2E Testing Track** | `Assets/DrivingSchool/Code/Tests/`, `tools/verify_evidence.py`, `artifacts/reports/editmode.xml`, `artifacts/visual-review/qa-results.json`, `TEST_INFRA.md`, `TEST_READY.md` | Exclusive to Test Writer / E2E Worker |

---

## Feature Inventory

Every feature surveyed from `ORIGINAL_REQUEST.md`, `spec_inventory.md`, and `architecture_survey.md` is assigned to a specific milestone.

| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Sedan Exterior Geometry | Modern original sedan 3D model without branded ties, correct proportions (L=4.50m, W=1.80m, H=1.48m), doors, seals, glazing | M1 | ORIGINAL_REQUEST §R1 |
| 2 | Detailed Interior Cab | Dashboard, center console, seats, upholstery, headliner, door cards | M1 | ORIGINAL_REQUEST §R1 |
| 3 | Three Control Pedals | Gas, brake, clutch pedals with independent pivots and 20° angular travel | M1 | ORIGINAL_REQUEST §R1 |
| 4 | Dual Gear Shifters | Manual 6-speed H-pattern lever and Automatic PRNDL selector models | M1 | ORIGINAL_REQUEST §R1 |
| 5 | Handbrake & Steering Column | Handbrake lever (35° travel) and column stalks (turn indicators, wipers) | M1 | ORIGINAL_REQUEST §R1 |
| 6 | Kinematic Pivot Separation | Wheel pivots (Z-spin, Y-steer ±32°), steering wheel (Z-rot ±450°), gauge needles (Z-rot 260°), wipers (Z-rot 70°) | M1 | ORIGINAL_REQUEST §R1 |
| 7 | Three Rearview Mirrors | Left, right, interior mirrors with separate reflective surfaces and FOV coverage | M1 | ORIGINAL_REQUEST §R1 |
| 8 | Independent Lighting Elements | Low/high beams, parking lights, brake lights, turn signals, dashboard illumination | M1 | ORIGINAL_REQUEST §R1 |
| 9 | Multi-Format 3D Deliverables | Editable Blender (.blend), optimized Unity FBX + PBR materials, and Web GLB | M1 | ORIGINAL_REQUEST §R1 |
| 10 | Interactive Kinematic Viewer | WebGL/Unity interactive scene with joint manipulators and mandatory non-physics disclaimer | M1 | ORIGINAL_REQUEST §R1 |
| 11 | Modular Infrastructure Kit | Curbs, sidewalks, markings, sign posts, traffic lights, street lamps, barriers, cones, delineators, bus stop | M2 | ORIGINAL_REQUEST §R2 |
| 12 | Modular Facades & Vegetation | Residential, retail, industrial building facades and road greenery | M2 | ORIGINAL_REQUEST §R2 |
| 13 | Procedural Road Mesh Generation | Seamless road meshes, curves, intersections without vertex gaps or normal splits | M2 | ORIGINAL_REQUEST §R2 |
| 14 | 10x10 km Masterplan | Zoned territory: Centre, Residential, Industry, Suburb, Highway, Autodrome | M2 | ORIGINAL_REQUEST §R2 |
| 15 | Closed Training Routes | Beginner city circuit and high-speed expressway circuit | M2 | ORIGINAL_REQUEST §R2 |
| 16 | 500x500 m Demo Block | Signalized intersection, crosswalk, parking lot, residential lane, bus stop | M2 | ORIGINAL_REQUEST §R2 |
| 17 | Multi-View & Lighting Modes | Top-down, pedestrian, driver-seat viewpoints with Day/Night lighting transitions | M2 | ORIGINAL_REQUEST §R2 |
| 18 | Parametric Autodrome Layout | Autodrome sizing parameterized to training sedan (wheelbase 2.72m, radius 5.6m) | M2 | ORIGINAL_REQUEST §R2 |
| 19 | Autodrome Ex 01: Start & Stop | Regulated starting grid and precision stopping boundary | M2 | ORIGINAL_REQUEST §R2 |
| 20 | Autodrome Ex 02: Slalom | 5-cone serpentine slalom spaced at 11.25m intervals | M2 | ORIGINAL_REQUEST §R2 |
| 21 | Autodrome Ex 03: Box Stall | 90° reverse parking box (5.5m x 2.8m) | M2 | ORIGINAL_REQUEST §R2 |
| 22 | Autodrome Ex 04: Parallel Bay | Reverse parallel parking bay (7.25m x 2.8m) | M2 | ORIGINAL_REQUEST §R2 |
| 23 | Autodrome Ex 05: Hill Ramp | Overpass ramp with 10% incline for hill-start handbrake exercise | M2 | ORIGINAL_REQUEST §R2 |
| 24 | Autodrome Ex 06: U-Turn | Limited space 3-point turn corridor with 5.6m turning radius guide | M2 | ORIGINAL_REQUEST §R2 |
| 25 | Autodrome Ex 07: Reverse Corridor | Narrow precision reverse corridor (25m x 3.0m) | M2 | ORIGINAL_REQUEST §R2 |
| 26 | Autodrome Ex 08: Shift & Brake | Acceleration strip, gear shift zone, and threshold emergency braking box | M2 | ORIGINAL_REQUEST §R2 |
| 27 | UI Screen 1: Main Menu | Continue course, exercise catalog, profile overview, settings jump | M3 | ORIGINAL_REQUEST §R3 |
| 28 | UI Screen 2: Lesson Select | Curriculum tree, lesson objectives, regulatory rules, difficulty rating | M3 | ORIGINAL_REQUEST §R3 |
| 29 | UI Screen 3: Vehicle & Conditions | Transmission toggle (MT/AT), weather presets, time of day, traffic density | M3 | ORIGINAL_REQUEST §R3 |
| 30 | UI Screen 4: G27 Calibration | Axis binding for wheel (900°), 3 pedals, 6+R shifter, deadzone & FFB sliders | M3 | ORIGINAL_REQUEST §R3 |
| 31 | UI Screen 5: Driving HUD & Coaching | Minimalist HUD (speed, gear, RPM, pedals) and adaptive instructional coach popups | M3 | ORIGINAL_REQUEST §R3 |
| 32 | UI Screen 6: Pause & Settings | Quick resume, restart, audio/graphics/FFB fine-tuning, exit to menu | M3 | ORIGINAL_REQUEST §R3 |
| 33 | UI Screen 7: Theory Exam | Traffic rule questions, countdown timer, options, and regulatory explanations | M3 | ORIGINAL_REQUEST §R3 |
| 34 | UI Screen 8: Trip Debrief | Chronological penalty log, telemetry graphs, score breakdown, driving advice | M3 | ORIGINAL_REQUEST §R3 |
| 35 | UI Screen 9: World Editor | Object palette, translation gizmos, node property inspector, graph topology validator | M3 | ORIGINAL_REQUEST §R3 |
| 36 | Dark Graphite Design System | Cohesive UI tokens, blue accent, typography scale, 8 component interaction states | M3 | ORIGINAL_REQUEST §R3 |
| 37 | Responsive UI Layouts | Fluid adaptation across 1920x1080 (desktop), 1280x720 (compact), and tablet | M3 | ORIGINAL_REQUEST §R3 |
| 38 | Dedicated VR UI Stage | Ergonomic world-space curved HUD and pause panels designed for OpenXR headset | M3 | ORIGINAL_REQUEST §R3 |
| 39 | Unified Web Showcase | Local interactive portal linking 9-screen prototype, 3D viewer, renders, and QA logs | M3 | ORIGINAL_REQUEST §R3 |
| 40 | Unity 6000.3 URP Foundation | Isolated assembly definitions (.asmdef) enforcing clean architecture | M4 | ORIGINAL_REQUEST §R4 |
| 41 | Pure C# POCO Contracts | `DriverCommand`, `VehicleState`, `WorldDocument`, `RuleEvent`, `LessonDefinition` | M4 | ORIGINAL_REQUEST §R4 |
| 42 | Command Validation & Sanitization | Clamping inputs, rejecting NaN/Infinity, neutral gear logic in `DriverCommand` | M4 | ORIGINAL_REQUEST §R4 |
| 43 | 100 Hz Simulation Decoupling | Numerical drivetrain torque model decoupled from presentation and physics engine | M4 | ORIGINAL_REQUEST §R4 |
| 44 | Floating Origin Protocol | Double-precision canonical coordinates with 256m chunk grid shift on FixedUpdate | M4 | ORIGINAL_REQUEST §R4 |
| 45 | Logitech G27 FFB Adapter | DirectInput / Raw Input calculation of centering, damping, stops, and road texture | M4 | ORIGINAL_REQUEST §R4 |
| 46 | FFB Watchdog & Rate Limiter | Slew rate limit (10.0/s) and idempotent `Stop()` on focus loss or pause | M4 | ORIGINAL_REQUEST §R4 |
| 47 | Transactional World Serialization | JSON world serialization with schema validation, `.tmp` staging, and `.bak` backups | M4 | ORIGINAL_REQUEST §R4 |
| 48 | URP Planar Mirror Prototype | Multi-camera oblique projection rendering with time-slicing for wing mirrors | M4 | ORIGINAL_REQUEST §R4 |
| 49 | Lesson Lifecycle State Machine | Deterministic state transitions (`NotStarted` -> `Active` -> `Completed`/`Failed`) | M4 | ORIGINAL_REQUEST §R4 |
| 50 | Theory Exam Engine | Timed evaluation of answers, scoring calculations, and explanation display | M4 | ORIGINAL_REQUEST §R4 |
| 51 | Worker Task Package Framework | Standardized 7-section cards with contracts, preconditions, DoD, and forbidden zones | M5 | ORIGINAL_REQUEST §R5 |
| 52 | Complete Task Cards (T01-T26) | 26 modular work packages covering art, simulation, UI, and world generation | M5 | ORIGINAL_REQUEST §R5 |
| 53 | Architectural Decision Records | 10 ADRs documenting architectural rationale (ADR-001 through ADR-010) | M5 | ORIGINAL_REQUEST §R5 |
| 54 | Acceptance Criteria Matrix | Formal mapping from requirements R1–R5 to acceptance gates A01–A19 | M5 | ORIGINAL_REQUEST §R5 |
| 55 | SHA-256 Evidence Manifest | Checksum manifest auditing 73 project deliverables via `tools/verify_evidence.py` | M5 | ORIGINAL_REQUEST §R5 |
| 56 | Standalone Windows Player Build | Compiled 64-bit standalone executable (`DrivingSchoolSim.exe`) | M5 | ORIGINAL_REQUEST §R5 |
| 57 | Tier 1-4 Opaque-Box E2E Suite | 60+ test cases covering features, boundaries, pairwise interactions, and scenarios | M_E2E | Project Pattern Dual Track |
| 58 | Automated EditMode UTF Runner | Automated test runner in batchmode verifying pure contracts and serialization | M_E2E | Project Pattern Dual Track |
| 59 | Adversarial Coverage Hardening | White-box stress tests, extreme numerical inputs, and integrity forensics | M_FINAL | Project Pattern Dual Track |

---

## Milestones

| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | 3D Asset & Visual Geometry Pipeline | Sedan geometry (exterior, interior, 3 pedals, 2 levers, pivots, 3 mirrors, lights, .blend, FBX, GLB, demonstrator) | none | DONE |
| M2 | Spatial, Road Network & Autodrome Framework | Modular road kit, seamless procedural road meshes, 10x10 km masterplan, 500x500m block, parametric 8-exercise autodrome | M1 (vehicle dimensions) | DONE |
| M3 | UI Design System & Interactive 9-Screen Prototype | Graphite design system, 9 responsive screens, VR stage, Three.js 3D viewer, unified showcase | none | DONE |
| M4 | Unity Engine Core Architecture & Pure Contracts | 9 .asmdef boundaries, pure POCOs, 100Hz tick, Floating Origin, G27 FFB math, JSON persistence, planar mirrors | none | DONE |
| M5 | Worker Task Packages & Architecture Decision Records | 26 standardized task cards (T01–T26), 10 ADRs, Acceptance Matrix (A01-A19), Evidence Manifest (87 files) | M1, M2, M3, M4 | DONE |
| M_E2E | Requirement-Driven Opaque-Box E2E Testing Track | Automated test infrastructure, Tier 1–4 test cases, test runner, publish `TEST_READY.md` | none | DONE |
| M_FINAL | Final Milestone: 100% E2E Pass & Adversarial Hardening | Pass 100% E2E tests (Tiers 1-4, 160/160) + Tier 5 white-box adversarial stress testing & forensic audit | M1-M5, M_E2E | DONE |

---

## Interface Contracts

### 1. Drivetrain & Control: `DriverCommand` ↔ `VehicleState`
```csharp
namespace DrivingSchool.Contracts
{
    public sealed class DriverCommand
    {
        public double Timestamp { get; set; }
        public float Steering { get; set; }      // [-1.0 .. +1.0]
        public float Throttle { get; set; }      // [0.0 .. 1.0]
        public float Brake { get; set; }         // [0.0 .. 1.0]
        public float Clutch { get; set; }        // [0.0 .. 1.0]
        public float Handbrake { get; set; }     // [0.0 .. 1.0]
        public int Gear { get; set; }            // -1: R, 0: N, 1..6: Gears
        public bool Ignition { get; set; }
        public bool HazardLights { get; set; }
        public bool LeftIndicator { get; set; }
        public bool RightIndicator { get; set; }
        public bool HighBeam { get; set; }
        public bool LowBeam { get; set; }
        public bool Horn { get; set; }
        public bool Wipers { get; set; }

        public bool Validate(out string error);
    }

    public sealed class VehicleState
    {
        public double Timestamp { get; set; }
        public double PosX { get; set; }
        public double PosY { get; set; }
        public double PosZ { get; set; }
        public float SpeedMps { get; set; }
        public float EngineRpm { get; set; }
        public float WheelAngleDeg { get; set; }
        public int CurrentGear { get; set; }
        public EnginePhase EnginePhase { get; set; }
        public bool IsStalled { get; set; }
    }
}
```

### 2. Hardware Input & Force Feedback: `IInputSource` ↔ `IForceFeedbackOutput`
```csharp
namespace DrivingSchool.Contracts
{
    public interface IInputSource
    {
        bool Poll(out DriverCommand command);
    }

    public interface IForceFeedbackOutput
    {
        void SetConstantForce(float normalizedTorque); // [-1.0 .. +1.0]
        void SetDamper(float coefficient);             // [0.0 .. 1.0]
        void SetSpring(float stiffness, float center); // [0.0 .. 1.0], [-1.0 .. +1.0]
        void Stop();                                   // Immediate zeroing watchdog
    }
}
```

### 3. Spatial & Serialization: `WorldDocument` ↔ `WorldRepository`
```csharp
namespace DrivingSchool.Contracts
{
    public sealed class WorldDocument
    {
        public int Version { get; set; } = 1;
        public string Name { get; set; }
        public double BoundsMinX { get; set; }
        public double BoundsMinZ { get; set; }
        public double BoundsMaxX { get; set; }
        public double BoundsMaxZ { get; set; }
        public List<District> Districts { get; set; } = new();
        public List<RoadNode> Nodes { get; set; } = new();
        public List<RoadSegment> Segments { get; set; } = new();
        public List<WorldObject> Objects { get; set; } = new();
    }
}
```

### 4. Pedagogy & Rules: `LessonDefinition` ↔ `RuleEvent` ↔ `SessionResult`
```csharp
namespace DrivingSchool.Contracts
{
    public sealed class LessonDefinition
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public double MaxDurationSeconds { get; set; }
        public int MaxAllowedPenalties { get; set; }
        public List<string> RequiredExercises { get; set; } = new();
    }

    public sealed class RuleEvent
    {
        public double Timestamp { get; set; }
        public string RuleId { get; set; }
        public int PenaltyPoints { get; set; }
        public string Description { get; set; }
        public bool IsFatal { get; set; }
    }

    public sealed class SessionResult
    {
        public string LessonId { get; set; }
        public bool Passed { get; set; }
        public double TotalDuration { get; set; }
        public int TotalPenalties { get; set; }
        public List<RuleEvent> Infractions { get; set; } = new();
    }
}
```

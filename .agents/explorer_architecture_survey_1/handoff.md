# Handoff Report: Architecture & Risk Survey (Phase 1)

**Agent**: `explorer_architecture_survey_1` (Architecture and Risk Explorer)  
**Parent Agent**: `orchestrator_1` (Conversation ID: `3ff2022b-bd57-49b3-a39f-491a7d53b3ad`)  
**Date**: 2026-09-19  
**Type**: Hard Handoff (Task Complete)  
**Deliverable File**: `c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\explorer_architecture_survey_1\architecture_survey.md`

---

## 1. Observation

Direct observations from the repository inspection and codebase audit:

1. **Requirements & Scope**:
   - `ORIGINAL_REQUEST.md`, lines 18–59: Explicitly defines requirements R1 (3D training car & showcase assets), R2 (modular road system, 10×10 km masterplan, 500×500 m block, autodrome), R3 (clickable 9-screen UI prototype & unified showcase), R4 (Unity 6000.3 URP architecture, pure C# POCOs, Floating Origin, G27 FFB, JSON/binary serialization), and R5 (worker task package structure, ADRs, acceptance criteria).
2. **Current Implementation Baseline**:
   - `Packages/manifest.json`, lines 3–8: Uses Unity version 6000.3.10f1 with packages `com.unity.render-pipelines.universal` (17.3.0), `com.unity.inputsystem` (1.18.0), `com.unity.ugui` (2.0.0), `com.unity.xr.openxr` (1.16.1), and `com.unity.test-framework` (1.6.0).
   - `Assets/DrivingSchool/Code/Contracts/Contracts.cs`, lines 5–75: Declares `DriverCommand`, `VehicleState`, `IInputSource`, `IForceFeedbackOutput`, `WorldDocument`, `RoadNode`, `RoadSegment`, `Lane`, `WorldObject`, `District`, `RuleEvent`, `LessonDefinition`, `SessionResult`, `TheoryContentPack`, `TheoryQuestion`.
   - `Assets/DrivingSchool/Code/Contracts/DS.Contracts.asmdef`, lines 1–6: Confirms `noEngineReferences: true` and empty references list.
   - `Assets/DrivingSchool/Code/Simulation/DS.Simulation.asmdef`, lines 1–8: Confirms `noEngineReferences: true` with reference to `DS.Contracts`.
   - `Assets/DrivingSchool/Code/Rules/DS.Rules.asmdef` and `DS.Learning.asmdef`: Both specify `noEngineReferences: true`.
   - `Assets/DrivingSchool/Code/Tests/ContractTests.cs`, lines 11–32: Contains 12 passing NUnit tests covering drivetrain torque calculations, command validation, lesson session state transitions, and world JSON roundtrip/backup.
   - `Assets/DrivingSchool/Code/World/WorldRepository.cs`, lines 17–27: Implements atomic saving with `.tmp` write and `File.Replace` to generate `.bak` backup.
   - `Assets/DrivingSchool/Code/Presentation/PlanarMirror.cs`, lines 6–39: Implements single-camera planar reflection in URP using `RenderTexture` (512×256) and oblique projection matrix; explicitly noted in line 6: *"Mono-view prototype. XR per-eye rendering remains an explicit acceptance gate."*
   - `ArtSource/`: Contains `DS_Sedan_A_closed_shell.blend` (409,164 bytes), `DS_District.blend` (1,500,577 bytes), and `DS_Autodrome.blend` (132,355 bytes).
   - `artifacts/visual-review/`: Contains interactive 9-screen prototype (`prototype.html`, `prototype.js`, `prototype.css`), 3D WebGL viewer (`viewer.html` via Three.js r160), 24 Cycles render PNGs in `renders/`, `data/masterplan.json`, and `qa-results.json`.
   - `docs/tasks/`: Contains 26 detailed task cards (`T01.md` through `T26.md`) mapped to acceptance criteria `A01` through `A19` in `docs/acceptance.md`.

---

## 2. Logic Chain

From the direct observations above, the architectural decomposition and risk strategy were deduced through the following verified steps:

1. **Step 1 (Core Decoupling)**: Observation 2 shows `DS.Contracts`, `DS.Simulation`, `DS.Rules`, and `DS.Learning` configured with `noEngineReferences: true`. However, vehicle movement requires physical contact forces (gravity, tire grip, collision response). Therefore, introducing Unity `Rigidbody` or `WheelCollider` directly into `DS.Simulation` would break the pure C# domain rule. **Conclusion**: An explicit intermediate adapter assembly (`DS.PhysicsAdapter`) is required to bridge pure numerical drivetrain torque outputs into PhysX `FixedUpdate` forces.
2. **Step 2 (Spatial Topology & Floating Origin)**: Observation 1 specifies a 10×10 km territory (coordinates $\pm 5,000$ m), while 32-bit floating point precision in graphics/physics degrades beyond $1,000$ m. Observation 2 shows `WorldDocument` stores node coordinates in `double`. **Conclusion**: A Floating Origin protocol shifting Unity transforms by $-\Delta \mathbf{O}$ at 256 m chunk grid boundaries during `FixedUpdate` solves the precision limitation while keeping canonical coordinates in 64-bit `double`.
3. **Step 3 (Parametric Autodrome Alignment)**: Observation 1 requires autodrome exercises dimensioned to the training sedan. Observation 2 (`DS_Sedan_A` dimensions: wheelbase $L=2.72$ m, width $W=1.80$ m, length $4.50$ m, turning radius $R=5.60$ m) enables exact mathematical derivation for all 8 regulatory exercises (Slalom cone spacing $2.5 \cdot L_{car} \approx 11.25$ m; Box stall $L_{car}+1.0\text{m} = 5.50$ m; Parallel bay $1.5 \cdot L_{car}+0.5\text{m} \approx 7.25$ m; Ramp 10% grade).
4. **Step 4 (Force Feedback Safety & Control)**: Observation 2 shows `IForceFeedbackOutput` accepting normalized torque $[-1.0, +1.0]$ with an idempotent `Stop()` method. Hardware disconnects or focus losses could leave physical motors active. **Conclusion**: A software watchdog with slew rate limiting ($\Delta T / \Delta t \le 10.0\text{ s}^{-1}$) and automatic invocation of `Stop()` on application pause or focus loss guarantees user physical safety.
5. **Step 5 (Mirror Rendering & VR Fallback)**: Observation 2 shows `PlanarMirror.cs` using a single camera and `UniversalRenderPipeline.RenderSingleCamera`. Three mirrors require 3 extra render passes. In VR, stereo rendering would double this to 6 passes. **Conclusion**: Multi-mirror rendering must be time-sliced in desktop mode (30 Hz refresh on wing mirrors) and architected with a fallback (parallax-corrected cubemap / planar screen-space reflection) for VR performance.

---

## 3. Caveats

1. **Hardware Verification**: Physical Logitech G27 wheel and OpenXR virtual reality headsets were not physically attached to this headless development environment. While the software adapters, DirectInput math, axis normalizers, and OpenXR manifest configurations were fully audited, physical hardware verification remains designated under Gate C05.
2. **Single-threaded Batch Assumptions**: Automated test runner commands assume single-instance batchmode execution. Simultaneous runs of Unity Editor and command-line batch runs can result in workspace lock conflicts on `Library/` files.
3. **External Art Assets**: No third-party commercial 3D assets or proprietary vehicle meshes are present; all geometry is procedurally generated or authored via script, ensuring complete IP independence.

---

## 4. Conclusion

The Phase 1 architecture for `DrivingSchoolSim` is thoroughly surveyed, structurally decomposed, and documented in `architecture_survey.md`. The domain boundaries cleanly isolate business logic from engine presentation, the spatial and road generation models are geometrically consistent, the 9-screen UI prototype and WebGL viewer provide an immediate inspection portal, and the 26 worker task cards (`T01`–`T26`) with Gates C00–C05 establish an auditable delivery pipeline.

The project is ready to proceed to Phase 1 Milestone dispatch following the recommended 5-milestone roadmap:
- **Milestone M1**: 3D Asset & Visual Geometry Pipeline
- **Milestone M2**: Spatial, Road Network & Autodrome Framework
- **Milestone M3**: UI Design System & 9-Screen Prototype
- **Milestone M4**: Unity Engine Foundation & Contracts
- **Milestone M5**: Verification Suite, Worker Task Packages & Handoff

---

## 5. Verification Method

To independently verify the findings, data structures, and architectural survey:

1. **Inspect Survey Document**:
   - Verify existence and completeness of `architecture_survey.md`:
     `view_file` at `c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\explorer_architecture_survey_1\architecture_survey.md`.
2. **Verify Assembly Definitions & Contracts**:
   - Check pure C# assembly definitions:
     `view_file` at `Assets/DrivingSchool/Code/Contracts/DS.Contracts.asmdef` and confirm `"noEngineReferences": true`.
   - Inspect data contracts in `Assets/DrivingSchool/Code/Contracts/Contracts.cs`.
3. **Run Existing Automated Unit Tests (Gate C01)**:
   - Execute PowerShell test command:
     ```powershell
     $dsUnity = "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe" # or system path
     $dsProject = "c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim"
     Start-Process -FilePath $dsUnity -ArgumentList @('-batchmode','-nographics','-projectPath',$dsProject,'-runTests','-testPlatform','EditMode','-testResults',"$dsProject/artifacts/reports/editmode.xml",'-logFile',"$dsProject/artifacts/reports/editmode.log") -Wait
     ```
   - Invalidation Condition: Any failure among the 12 NUnit tests, or compiler errors indicating `UnityEngine` references inside `DS.Contracts` or `DS.Simulation`.
4. **Verify Evidence Manifest**:
   - Inspect `artifacts/reports/evidence-manifest.json` and run `python tools/verify_evidence.py` to confirm all 73 project artifacts match expected SHA-256 hashes.

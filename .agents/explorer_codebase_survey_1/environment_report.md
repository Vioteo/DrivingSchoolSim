# Driving School Simulator (Phase 1) — Environment and Codebase Survey Report

- **Date**: 2026-09-19
- **Investigator**: Codebase and Tooling Explorer (`explorer_codebase_survey_1`)
- **Workspace**: `C:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim`
- **Host OS**: Windows 11 Professional (10.0.26200 x64)
- **Status**: Live Environment Verified (Unity, Blender, Python, Node.js, MSVC, UTF Test Runner)

---

## Executive Summary

The project workspace contains an active Unity 6000.3 URP project with existing modular architecture, comprehensive documentation, 3D source files in Blender, exported FBX/GLB models, an interactive 9-screen HTML/WebGL visual showcase, and verified batch builds.

Live verification confirmed:
1. **Unity Editor 6000.3.10f1** is installed on drive `E:` (`E:\unityroot\6000.3.10f1\Editor\Unity.exe`) and successfully compiles all assemblies with exit code 0.
2. **Unity Test Framework (UTF 1.6.0 / NUnit)** was executed live in batchmode; all **12 EditMode tests passed** in 25.3s.
3. **Blender 5.0.1** is installed at `C:\Program Files\Blender Foundation\Blender 5.0\blender.exe`; headless Python execution (`bpy`) was verified live.
4. **Python 3.13.3** is in PATH with `numpy`, `scipy`, and `pytest`.
5. **Node.js v24.19.0** and **npm 11.17.0** are installed; Three.js is vendored locally in `artifacts/visual-review/vendor/`.
6. **Compilers**: MSVC C++ 14.44 (`cl.exe`), Roslyn C# (`csc.exe` v4.14.0), and MSBuild v17.14 are installed with Visual Studio 2022 Community.
7. **Git**: Git 2.51.0 is installed inside Visual Studio, but the project directory is **not yet initialized as a git repository** (no `.git` directory, though `.gitignore` exists).

---

## 1. Directory Structure and Project Artifacts

### 1.1 Project Root Layout
```
DrivingSchoolSim/
├── .agents/                 # Multi-agent coordination metadata and survey working directories
├── .gitignore               # Standard Unity gitignore (excludes Library, Temp, Builds, etc.)
├── ORIGINAL_REQUEST.md      # Authoritative project prompt and requirements (R1–R5)
├── README.md                # Project overview, requirements roadmap, and commands reference
├── ArtSource/               # 8 Blender (.blend/.blend1) source files
├── Assets/                  # Unity asset tree (Code, Art, Materials, Prefabs, Scenes, etc.)
├── Builds/                  # Compiled player builds (Builds/Windows/DrivingSchoolSim.exe)
├── Library/                 # Unity cached package and script compilation state
├── Logs/                    # Unity editor logs
├── Packages/                # Unity package manifest (manifest.json, packages-lock.json)
├── ProjectSettings/         # Unity settings (ProjectVersion.txt = 6000.3.10f1)
├── UserSettings/            # Editor user preferences
├── artifacts/               # Visual review showcase, test logs, geometry JSONs, player smoke logs
├── docs/                    # Architecture, ADRs, tasks (T01–T26), standards, current-state
└── tools/                   # 11 Python automation scripts (geometry generation, verification, setup)
```

### 1.2 3D Art Assets (`ArtSource/` & `Assets/DrivingSchool/Art/`)
- **Blender Source Files (`ArtSource/`)**:
  - `DS_Sedan_A.blend` (372 KB) — Original sedan v2 source model.
  - `DS_Sedan_A_closed_shell.blend` (409 KB) — Repaired sedan shell with closed gaps.
  - `DS_District.blend` (1,500 KB) — 500×500 m demonstration district.
  - `DS_Autodrome.blend` (132 KB) — Parametric autodrome with 8 exercises.
  - `DS_Sedan_A_reviewed.blend`, `DS_Sedan_A_v1.blend` — Prior iterations.
- **Exported Unity FBX (`Assets/DrivingSchool/Art/`)**:
  - `DS_Sedan_A.fbx` (3.10 MB, 387 parts, bounds: 2.25 × 1.48 × 4.68 m).
  - `DS_Sedan_A_closed_shell.fbx` (3.19 MB).
  - `DS_District.fbx` (9.99 MB).
  - `DS_Autodrome.fbx` (637 KB).
  - `palette.json` (6.27 KB) — Material definition mapping.
- **Web 3D GLB Models (`artifacts/visual-review/models/`)**:
  - `DS_Sedan_A.glb` (3.47 MB), `DS_Sedan_A_closed_shell.glb` (3.66 MB), `DS_District.glb` (6.24 MB), `DS_Autodrome.glb` (797 KB).

### 1.3 Scenes, Prefabs & Materials (`Assets/DrivingSchool/`)
- **Scenes (`Scenes/`)**:
  - `Autodrome.unity` (12.9 KB)
  - `District.unity` (13.9 KB)
  - `Showroom.unity` (20.6 KB)
- **Prefabs (`Prefabs/`)**:
  - `DS_Sedan_A.prefab` (6.65 KB)
- **Materials (`Materials/`)**:
  - 58 URP material assets (`.mat`) configured with URP Lit and Unlit shaders.
- **StreamingAssets (`Assets/StreamingAssets/Examples/`)**:
  - `world.json` (5.5 KB) — 5 nodes, 4 segments, 16 lanes demo road network.
  - `lesson.json` (301 B) — Target distance 20m, stop speed 0.14 m/s.
  - `theory.json` (963 B) — Theory question and explanation.
  - `vehicle.json` (592 B) — Physical parameters specification.

---

## 2. Unity & .NET Tooling Analysis

| Component | Path / Command | Detected Version | Status / Notes |
|---|---|---|---|
| **Unity Editor** | `E:\unityroot\6000.3.10f1\Editor\Unity.exe` | 6000.3.10f1 (`e35f0c77bd8e`) | **VERIFIED** — Batchmode compile & tests pass |
| **Unity Hub CLI** | `C:\Program Files\Unity Hub\resources\cli\unity.exe` | 1.0.0 | Present |
| **Project Target** | `ProjectSettings/ProjectVersion.txt` | 6000.3.10f1 | Exact version match |
| **URP Package** | `com.unity.render-pipelines.universal` | 17.3.0 | Configured in `manifest.json` |
| **Input System** | `com.unity.inputsystem` | 1.18.0 | Installed |
| **OpenXR** | `com.unity.xr.openxr` | 1.16.1 | Installed |
| **XR Management**| `com.unity.xr.management` | 4.5.4 | Installed |
| **Test Framework**| `com.unity.test-framework` | 1.6.0 | Installed |
| **dotnet CLI** | `C:\Program Files\dotnet\dotnet.exe` | Host 8.0.21 | **Runtimes only**: .NETCore 3.1.8, 6.0.11, 8.0.21; WinDesktop 3.1, 6.0. **No modern .NET SDK** installed for dotnet CLI |
| **Visual Studio** | `C:\Program Files\Microsoft Visual Studio\2022\Community` | 17.14.27 (Feb 2026) | Installed (Community Edition) |
| **MSBuild** | `...\MSBuild\Current\Bin\amd64\MSBuild.exe` | 17.14.40.60911 | .NET Framework 4.8 / Roslyn support |
| **C# Compiler** | `...\MSBuild\Current\Bin\Roslyn\csc.exe` | 4.14.0-3.25412.6 | Roslyn compiler functional |

---

## 3. C# Codebase & Assembly Architecture

The C# codebase is strictly partitioned into isolated assemblies via `.asmdef` files with clean boundary definitions:

| Assembly | Root Namespace | Engine Reference? | Target Platform | Dependencies | Implemented Files |
|---|---|---|---|---|---|
| **DS.Contracts** | `DrivingSchool.Contracts` | `noEngineReferences: true` | Any | None | `Contracts.cs` (76 lines: `DriverCommand`, `VehicleState`, `IInputSource`, `IForceFeedbackOutput`, `WorldDocument`, etc.) |
| **DS.Simulation**| `DrivingSchool.Simulation` | `noEngineReferences: true` | Any | `DS.Contracts` | `DrivetrainMath.cs` (AxleTorque, ClutchTorque) |
| **DS.Learning** | `DrivingSchool.Learning` | `noEngineReferences: true` | Any | `DS.Contracts` | `LessonSession.cs` (Lesson state machine: Briefing → Ready → Running → Passed/Failed/Cancelled) |
| **DS.World** | `DrivingSchool.World` | Engine-dependent (JsonUtility) | Any | `DS.Contracts` | `WorldRepository.cs`, `WorldValidator.cs` (Roundtrip serialization, schema validation) |
| **DS.Presentation**| `DrivingSchool.Presentation` | Engine-dependent | Any | Contracts, Simulation, World, Input, Rules, Learning, InputSystem, URP | `ModelDemonstrator.cs` (IMGUI inspection sliders), `PlanarMirror.cs` (Oblique projection mirror camera) |
| **DS.Editor** | `DrivingSchool.Editor` | Engine-dependent | Editor only | Contracts, Simulation, World, Learning, Presentation, URP, InputSystem, XR | `ProjectBuilder.cs` (`ProjectBuilder.Prepare`, `ProjectBuilder.Build`) |
| **DS.Tests** | `DrivingSchool.Tests` | Engine-dependent (TestAssemblies) | Editor only | Contracts, Simulation, World, Learning | `ContractTests.cs` (12 NUnit tests) |
| **DS.Input** | `DrivingSchool.Input` | Engine-dependent | Any | Contracts, InputSystem | *Empty asmdef (no .cs files yet)* |
| **DS.Rules** | `DrivingSchool.Rules` | `noEngineReferences: true` | Any | Contracts | *Empty asmdef (no .cs files yet)* |

---

## 4. Test Runners, Compilers, and Packaging Tools

### 4.1 Test Execution Verification
- **Unity Test Framework (UTF)**:
  - **Live Command Executed**:
    ```powershell
    & 'E:\unityroot\6000.3.10f1\Editor\Unity.exe' -batchmode -nographics -projectPath 'C:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim' -runTests -testPlatform EditMode -testResults '...\test_output\editmode_survey.xml' -logFile '...\test_output\editmode_survey.log'
    ```
  - **Result**: `Process Exit Code: 0`, elapsed time: 25.32s.
  - **Test Statistics**: Total: **12**, Passed: **12**, Failed: **0**, Skipped: **0**.
  - **Tests Passed**:
    - `BrokenLaneSuccessorIsRejected`
    - `ClutchCannotExceedCapacity`
    - `CommandsRejectNan`
    - `DisengagedClutchCannotTransmitTorque`
    - `FutureSchemaIsNotSilentlyLoaded`
    - `HoldingStillAtSpawnDoesNotPass`
    - `InvalidStartIsRejected`
    - `NeutralCannotTransmitTorque`
    - `ReachingTargetThenStoppingPassesOnce`
    - `ReverseAndFinalDriveAffectTorque`
    - `TimeoutAndCancellationAreTerminal`
    - `WorldRoundTripAndBackup`
- **Python pytest**: Available via `python -m pytest`.
- **VS Test Runner**: `vstest.console.exe` available in VS 2022.

### 4.2 Compilers & Native Toolchains
- **C#**: Unity Bee/Tundra pipeline internally invokes Roslyn compiler. External Roslyn `csc.exe` v4.14 is also functional.
- **C++**: Microsoft Visual C++ 14.44 compiler (`cl.exe`) is present at `C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64\cl.exe`. Clang and GCC are not in PATH.
- **Standalone Build Packaging**: Standalone Windows player is functional. `Builds/Windows/DrivingSchoolSim.exe` (667 KB exe, ~177 MB data payload) was verified present and built via `ProjectBuilder.Build`.
- **Archive Utilities**: `tar.exe` and `curl.exe` are available in `C:\Windows\System32`.

---

## 5. 3D, Web, and Python Environment

### 5.1 Blender 3D (v5.0.1)
- **Path**: `C:\Program Files\Blender Foundation\Blender 5.0\blender.exe`
- **Version**: Blender 5.0.1 (built 2025-12-16)
- **Headless Execution**: Verified live with `--background --python`.
- **Mesh / Raycast Validation**: Verified via `check_shell_coverage.py` on `DS_Sedan_A_closed_shell.blend` (`SHELL_COVERAGE True 17`).

### 5.2 Python Environment (v3.13.3)
- **Path**: `C:\Users\AVSok\AppData\Local\Programs\Python\Python313\python.exe`
- **Installed Key Packages**: `numpy`, `scipy`, `pytest`, `fastapi`, `click`, `beautifulsoup4`, `cffi`, etc.
- **Artifact Verification Script**: `tools/verify_evidence.py` ran live and verified **73 of 73 evidence files present and matching SHA256 hashes**.

### 5.3 Web & UI Showcase
- **Node.js**: `C:\Program Files\nodejs\node.exe` (v24.19.0)
- **npm**: `C:\Program Files\nodejs\npm.cmd` (v11.17.0)
- **Vendored Three.js**: In `artifacts/visual-review/vendor/` (`three.core.js` 1.4 MB, `three.module.js` 603 KB, `GLTFLoader.js`, `OrbitControls.js`, `BufferGeometryUtils.js`).
- **Interactive Prototype**: `artifacts/visual-review/prototype.html` (9 screens: Home, Lessons, Vehicle Config, G27 Calibration, Driving HUD, Pause/Settings, Theory Quiz, Result Summary, World Editor + 2 VR previews).
- **Automation QA**: `artifacts/visual-review/qa-capture.cjs` configured for automated testing via Microsoft Edge headless browser.

---

## 6. Git Repository & Version Control Status

- **Git CLI**: Installed at `C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe` (`git version 2.51.0.windows.2`).
- **Path Status**: Git is currently not included in the global system/user PATH environment variable.
- **Repository Initialization**:
  - `Test-Path .git` returned `False`.
  - The project folder `c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim` is **not currently a git repository**.
  - A comprehensive `.gitignore` file is already in place.
  - Recommended action: Run `git init` using the VS Git executable if git version tracking is desired.

---

## 7. Operational Constraints & Hazards

1. **Unity Single-Process Project Lock**:
   - Unity uses `UnityLockfile` / shared lock while open. Launching batchmode commands (`-runTests`, `ProjectBuilder.Prepare`, `ProjectBuilder.Build`) while Unity Editor GUI is open will fail immediately with exit code 1. Any batch execution requires the Editor GUI to be closed first.
2. **Project Setup Script Warning**:
   - As documented in `README.md`, `tools/setup_project.py` will overwrite manifests, asmdefs, and sample data. It should **not** be run as a casual health-check command.
3. **Missing Assemblies Implementation**:
   - `DS.Input` and `DS.Rules` asmdefs currently have no `.cs` scripts. Unity logs warnings about these during compilation, but compilation succeeds with exit code 0.
4. **dotnet CLI SDK Absence**:
   - Running `dotnet build` from CLI will fail because no modern .NET SDK workload is registered in `C:\Program Files\dotnet\dotnet.exe`. All C# compilation and testing should be run through Unity batchmode or VS MSBuild (`C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe`).

---

## Summary Matrix

| Capability / Requirement | Available | Verified Live | Key Path / Tool |
|---|:---:|:---:|---|
| **Unity Project 6000.3** | Yes | Yes | `E:\unityroot\6000.3.10f1\Editor\Unity.exe` |
| **Unity Batch Compilation** | Yes | Yes | Exit code 0, 0 compilation errors |
| **EditMode Test Runner** | Yes | Yes | UTF 1.6.0 via Unity batchmode, 12/12 passed |
| **Windows Standalone Build** | Yes | Yes | `Builds/Windows/DrivingSchoolSim.exe` (177 MB) |
| **Blender 5.0 Headless (`bpy`)** | Yes | Yes | `C:\Program Files\Blender Foundation\Blender 5.0\blender.exe` |
| **Python 3.13 Runtime** | Yes | Yes | `C:\Users\AVSok\AppData\Local\Programs\Python\Python313\python.exe` |
| **Python Math / Test (`numpy`, `pytest`)** | Yes | Yes | Verified via python module import |
| **Node.js & npm** | Yes | Yes | Node v24.19.0, npm 11.17.0 |
| **Vendored Three.js & WebGL Viewer** | Yes | Yes | `artifacts/visual-review/vendor/` |
| **9-Screen Clickable UI Prototype** | Yes | Yes | `artifacts/visual-review/prototype.html` |
| **C++ MSVC Compiler (`cl.exe`)** | Yes | Yes | MSVC 14.44 in VS 2022 Community |
| **Git Version Control** | Tool Present | No Repo | VS Git 2.51.0 exists; `.git` directory not initialized |

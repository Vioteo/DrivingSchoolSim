# Adversarial Challenge Report — Phase 1 Challenger 2

**Date**: 2026-09-19  
**Auditor**: Challenger 2 (Empirical Challenger: critic, specialist)  
**Target Subsystems**:
1. World Serialization & Persistence (`WorldRepository.cs`, `WorldValidator.cs`, `world.json`)
2. Autodrome Parameterization Mathematics (`build_art.py`, `DS_Autodrome`, 8 exercises)
3. Masterplan Graph Topology (`masterplan.json`, `world.json`)

---

## Executive Summary & Verdict

### **VERDICT: REQUEST_CHANGES**

While foundational POCO data contracts and basic happy-path roundtrips are well implemented, code-executing adversarial stress testing has uncovered **two critical domain/mathematical defects** and **three persistence/validation vulnerabilities**:
1. **CRITICAL DEFECT**: The Hill Start stop line (`HillStop`) in `DS_Autodrome` is located at $y = -11.0\,\text{m}$ on the **flat horizontal plateau** ($z = 1.26\,\text{m}$, slope = 0.0%), rather than on the 10% incline ($y \in [-28, -16]\,\text{m}$). Consequently, the student vehicle stops on flat ground with 0 N rollback force, invalidating the exercise.
2. **HIGH DEFECT**: The 3D model compressed the Slalom cone spacing to **8.0 m** (deviating from the 11.25 m specified in `PROJECT.md` §F20). Kinematic simulation proves this limits the vehicle's turning arc to an unviable **3.9 cm clearance margin** at 100% steering lock ($R_{\min} = 5.60\,\text{m}$); any amplitude $A \le 1.10\,\text{m}$ produces an unavoidable collision with cones.
3. **MEDIUM VULNERABILITIES**:
   - `WorldRepository.Save()` lacks `try...finally` cleanup, leaking orphaned `.tmp` staging files whenever atomic replace fails (e.g., read-only destination or locked target).
   - `WorldRepository.Load()` fails immediately on corrupted/truncated JSON files without attempting recovery from the existing `.bak` backup file.
   - `WorldValidator.Validate()` does not validate `District` fields (permitting negative bounds and NaN) and accepts orphan/disconnected nodes with zero incident segments.

---

## 1. Quantitative Test Results

All tests were implemented in `tests/test_adversarial_challenger2.py` and executed via pytest:

| Test ID | Area | Adversarial Scenario | Outcome | Quantitative Finding |
|---|---|---|---|---|
| `test_adv_persistence_readonly_destination_failure_and_tmp_leak` | Persistence | Destination file marked read-only | PASSED (Detected) | Exception raised; orphaned `.tmp` file leaked on disk |
| `test_adv_persistence_simulated_crash_during_write` | Persistence | Process kill mid-write leaving truncated `.tmp` | PASSED | Target intact as Rev 1; subsequent save overwrites stale `.tmp` |
| `test_adv_persistence_disk_full_simulation` | Persistence | `OSError(28, ENOSPC)` during `.tmp` write | PASSED | Target file timestamp and content untouched |
| `test_adv_persistence_backup_chain_and_no_auto_rollback_vulnerability` | Persistence | Target truncated to 0 bytes with valid `.bak` | PASSED (Detected) | `Load()` crashes with `JSONDecodeError`; `.bak` ignored |
| `test_adv_schema_missing_required_fields` | Validation | Deleting `nodes`, `segments`, `lanes`, `objects`, `districts` | PASSED | `InvalidDataException("Missing array")` raised |
| `test_adv_schema_forward_compatibility_rejection` | Validation | `schemaVersion` $\in \{0, 2, 3, 99, -1\}$ | PASSED | `InvalidDataException("Unsupported world version")` raised |
| `test_adv_schema_orphan_disconnected_nodes_not_rejected_by_validator` | Validation | Floating node with degree 0 (`orphan_ghost_node`) | PASSED (Detected) | Validator passes silently; disconnected node retained |
| `test_adv_schema_unvalidated_district_properties` | Validation | District with `sizeM = -500` and `minX = NaN` | PASSED (Detected) | Validator passes silently; negative dimensions accepted |
| `test_adv_schema_invalid_lane_successors_and_cycles` | Validation | Successor `fromNode` $\ne$ lane `toNode` | PASSED | `InvalidDataException("fromNode != toNode")` raised |
| `test_adv_autodrome_bounding_boxes_inventory` | Autodrome | Overlap test across all 8 exercise bounding boxes | PASSED | All 8 bounding boxes are mutually disjoint |
| `test_adv_autodrome_ex02_slalom_kinematic_feasibility_defect` | Autodrome | Kinematic trajectory at actual 8m vs spec 11.25m | PASSED (Detected) | Actual: 3.9 cm margin at $R=5.60\,\text{m}$. Spec: $>50\,\text{cm}$ at $R=8.0\,\text{m}$ |
| `test_adv_autodrome_ex03_box_park_90_degree_reverse` | Autodrome | 90° reverse entry into $3.0 \times 9.0\,\text{m}$ stall from 20m aisle | PASSED | Front swing 7.435m fits in 20m aisle; mirror clearance 0.375m/side |
| `test_adv_autodrome_ex04_parallel_bay_solvability` | Autodrome | Reverse parallel entry into $3.0 \times 7.0\,\text{m}$ bay | PASSED | Free longitudinal margin 2.50m (ratio 1.55x car length); solvable |
| `test_adv_autodrome_ex05_hill_ramp_stop_line_defect` | Autodrome | Stop line position on ramp profile | PASSED (Detected) | Stop line at $y=-11.0\,\text{m}$ is on 0% plateau ($y \in [-16, -8]$) |
| `test_adv_autodrome_ex06_u_turn_swept_path` | Autodrome | 180° turn on 5.6m radius guide inside $28 \times 28\,\text{m}$ | PASSED | Swept path width 13.94m fits within 28.0m boundary |
| `test_adv_masterplan_route_topological_closure` | Masterplan | Closure of `beginner` and `highway` circuits | PASSED | Both routes strictly closed ($P_0 == P_{\text{end}}$) |
| `test_adv_masterplan_districts_spatial_disjointness` | Masterplan | Overlap check across 5 masterplan districts | PASSED | All 5 districts mutually disjoint in $10\times 10\,\text{km}$ |
| `test_adv_world_graph_open_crossroad_topology` | Topology | Graph connectivity of `world.json` demonstration block | PASSED | Star topology: 8 inbound lanes have successors; 8 outbound lanes dead-end |

**Execution Summary**: 18 tests passed in 0.18s. Full regression suite (`pytest tests`): 134 passed in 0.34s.

---

## 2. Detailed Vulnerability & Defect Reports

### Defect 1 (CRITICAL): Hill Ramp Stop Line (`HillStop`) Placed on Horizontal Plateau
- **Severity**: CRITICAL
- **Location**: `tools/build_art.py` lines 451–456, `Assets/DrivingSchool/Art/DS_Autodrome.fbx`, `Assets/DrivingSchool/Scenes/Autodrome.unity`
- **Code Observation**:
  ```python
  profile=[(-28,.06),(-16,1.26),(-8,1.26),(4,.06)]
  box('HillStop',(57,-11,1.271),(7,.25,.012),'Line_White',parent=r)
  ```
- **Physical Analysis**:
  - Incline section: $y \in [-28, -16]\,\text{m}$ ($z$ rises from $0.06\,\text{m}$ to $1.26\,\text{m}$, $\Delta z = 1.20\,\text{m}$, slope $= +10.0\%$).
  - Plateau section: $y \in [-16, -8]\,\text{m}$ ($z = 1.26\,\text{m}$, slope $= 0.0\%$).
  - Descent section: $y \in [-8, 4]\,\text{m}$ ($z$ falls from $1.26\,\text{m}$ to $0.06\,\text{m}$, slope $= -10.0\%$).
  - The stop marker `HillStop` is placed at $y = -11.0\,\text{m}$.
  - When the training sedan ($L = 4.50\,\text{m}$, wheelbase $WB = 2.72\,\text{m}$) halts with its front bumper at the stop line ($y = -11.0\,\text{m}$):
    - Front axle: $y = -11.89\,\text{m}$
    - Rear axle: $y = -14.61\,\text{m}$
    - Rear bumper: $y = -15.50\,\text{m}$
  - **All 4 wheels and the entire vehicle footprint are situated between $y = -15.50\,\text{m}$ and $y = -11.0\,\text{m}$, completely inside the flat plateau ($[-16, -8]\,\text{m}$)**.
  - The grade under the wheels is $0.0\%$. Gravity component along the road is $F = mg \sin(0) = 0\,\text{N}$. The car will not roll back, nullifying the hill-start handbrake balancing logic.
- **Required Mitigation**:
  Move `HillStop` to the incline: $(57, -20.0, 0.86)$, placing the entire vehicle on the 10% grade ($y \in [-24.5, -20.0]$).

---

### Defect 2 (HIGH): Slalom Cone Spacing Compressed to 8.0 m (Spec: 11.25 m)
- **Severity**: HIGH
- **Location**: `tools/build_art.py` line 447 vs `PROJECT.md` line 76 (`F20: 5-cone serpentine slalom spaced at 11.25m intervals`)
- **Code Observation**:
  ```python
  for y in (23,31,39,47,55): cone(-35,y,r)
  ```
  Actual spacing: $\Delta y = 8.0\,\text{m}$.
- **Kinematic Analysis**:
  - Sedan physical specs: $L = 4.50\,\text{m}$, $W_{\text{body}} = 1.80\,\text{m}$, $W_{\text{mirror}} = 2.25\,\text{m}$, $WB = 2.72\,\text{m}$, $R_{\min} = 5.60\,\text{m}$.
  - Cone base radius: $r_{\text{cone}} = 0.18\,\text{m}$.
  - In an alternating sinusoidal S-curve trajectory $x(y) = A \cos(\omega y)$ with wavelength $2 \Delta y = 16.0\,\text{m}$ ($\omega = \pi / 8$):
    - Peak curvature $\kappa = A \omega^2 = A (\pi/8)^2 \approx 0.1542 A$.
    - Turning radius at apex $R = 1 / \kappa$. To satisfy vehicle steering limit $R \ge 5.60\,\text{m}$:
      $$A \le \frac{1}{5.60 \times (\pi/8)^2} = 1.158\,\text{m}$$
    - At $A = 1.158\,\text{m}$, the vehicle's lateral center is offset by only $1.158\,\text{m}$ from the cone line.
    - Mirror half-width is $1.125\,\text{m}$; cone base radius is $0.18\,\text{m}$.
    - Minimum distance to cone surface across the full 2D swept polygon is **only 3.9 cm**!
    - If the driver steers with amplitude $A \le 1.10\,\text{m}$ (e.g. attempting a smoother turn with $R \approx 5.9\,\text{m}$), the distance becomes negative ($d = -2.8\,\text{cm}$), resulting in an immediate collision.
  - At the **specified 11.25 m interval**:
    $$\omega = \frac{\pi}{11.25} \approx 0.279\,\text{rad/m} \implies A_{\max} = \frac{1}{5.60 \times 0.279^2} = 2.29\,\text{m}$$
    At a realistic amplitude $A = 1.60\,\text{m}$, the turning radius is a comfortable $8.0\,\text{m}$ ($70\%$ of steering lock) and clearance to the cone exceeds **60 cm**.
- **Required Mitigation**:
  Re-space the 5 cones in `build_art.py` to 11.25 m intervals: $y \in [17.5, 28.75, 40.0, 51.25, 62.5]\,\text{m}$.

---

### Defect 3 (MEDIUM): Unhandled `.tmp` File Leakage on Write Failure
- **Severity**: MEDIUM
- **Location**: `Assets/DrivingSchool/Code/World/WorldRepository.cs` line 20–21, `tests/sim_contracts.py` line 295–305
- **Code Observation**:
  ```csharp
  var temp=path+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(world,true));
  if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);
  ```
- **Stress Test Finding**:
  When `path` exists but is marked read-only or locked by another process:
  1. `File.WriteAllText(temp, ...)` succeeds and creates `file.json.tmp`.
  2. `File.Replace` throws `UnauthorizedAccessException` or `IOException`.
  3. Because there is no `try...catch / finally` block, the exception propagates unhandled and **`file.json.tmp` is abandoned on disk**.
  4. Over time, failed saves litter the save directory with orphaned `.tmp` files.
- **Required Mitigation**:
  Wrap the commit in `try...catch`:
  ```csharp
  try {
      if(File.Exists(path)) File.Replace(temp, path, path+".bak");
      else File.Move(temp, path);
  } catch {
      if(File.Exists(temp)) try { File.Delete(temp); } catch {}
      throw;
  }
  ```

---

### Defect 4 (MEDIUM): Lack of Automatic Recovery from `.bak` on Corrupt Load
- **Severity**: MEDIUM
- **Location**: `Assets/DrivingSchool/Code/World/WorldRepository.cs` lines 23–27
- **Code Observation**:
  ```csharp
  public WorldDocument Load(string name)
  {
      var path=Resolve(name);if(!File.Exists(path))throw new FileNotFoundException("World not found",path);
      var w=JsonUtility.FromJson<WorldDocument>(File.ReadAllText(path));WorldValidator.Validate(w);return w;
  }
  ```
- **Stress Test Finding**:
  If the primary file `name.json` becomes zero bytes (e.g. sudden power loss during OS flush) or contains truncated JSON:
  - `Load()` crashes immediately with `JsonSerializationException` or `ArgumentException`.
  - The repository completely ignores `name.json.bak`, which contains the valid prior revision.
- **Required Mitigation**:
  Implement fallback in `Load`: if parsing or validating `name.json` fails, check if `name.json.bak` exists and attempt loading the backup before giving up.

---

### Defect 5 (LOW/MEDIUM): `WorldValidator` Omits District Checks & Orphan Node Warnings
- **Severity**: LOW / MEDIUM
- **Location**: `Assets/DrivingSchool/Code/World/WorldRepository.cs` line 37–44
- **Code Observation**:
  `WorldValidator.Validate` verifies that `w.districts != null`, but never iterates over `w.districts`. Districts with `sizeM <= 0`, NaN coordinates, or inverted bounds pass validation without error.
  Similarly, nodes that are not referenced by any segment (`fromNode` or `toNode`) are permitted, creating dangling spatial markers.
- **Required Mitigation**:
  Add validation loops for `District` (finite coordinates, positive size) and assert that all nodes in `nodes` are referenced by at least one road segment.

---

## 3. Recommended Remediation Plan

To transition this subsystem to `APPROVE`:
1. **Model & Art Update (M2)**:
   - In `tools/build_art.py`:
     - Move `HillStop` from $(57, -11, 1.271)$ to $(57, -20, 0.86)$.
     - Change slalom cone coordinates from $(23, 31, 39, 47, 55)$ to $(17.5, 28.75, 40.0, 51.25, 62.5)$.
     - Re-export `DS_Autodrome.blend` and `DS_Autodrome.fbx`.
2. **C# World Subsystem Hardening (M4)**:
   - Update `WorldRepository.cs`: add `try...finally` cleanup of `.tmp` files.
   - Update `WorldRepository.cs`: add `.bak` fallback recovery on JSON deserialization failure.
   - Update `WorldValidator.cs`: add district boundary validation and orphan node detection.
3. **Re-run Adversarial Test Suite**:
   - Verify all 18 tests in `tests/test_adversarial_challenger2.py` pass with positive physical clearances.

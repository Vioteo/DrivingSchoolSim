# Adversarial Stress Test Report — Phase 1 Challenger 1 (Replacement)

**Date**: 2026-09-19  
**Auditor**: Challenger 1 (Empirical Challenger: critic, specialist)  
**Target Subsystems**:
1. Drivetrain & Physics (`DrivetrainMath.cs`, `DriverCommand.cs`, `Contracts.cs`)
2. Logitech G27 FFB & Adapter (`LogitechG27Adapter.cs`)
3. Floating Origin (`FloatingOrigin.cs`)
4. Rule Evaluator (`RuleEvaluator.cs`, `TrafficRules.cs`, `SpeedLimitEvaluator.cs`)

---

## Executive Summary & Verdict

### **VERDICT: REQUEST_CHANGES**

Rigorous, code-executing adversarial stress testing and boundary fuzzing across the simulation, input, spatial, and pedagogy subsystems have uncovered **four critical defects** and **three high/medium architectural vulnerabilities**:

1. **CRITICAL DEFECT (Pedagogy & Timing)**: `RuleEvaluator.EvaluateStopLine` hardcodes `stopDurationAccumulated += 0.05` (Line 193) instead of utilizing simulation delta time (`dt = simSeconds - prevSeconds`). Under the target 100 Hz simulation loop ($dt = 0.01\,\text{s}$), a mandatory 1.0-second regulatory stop is satisfied in only **20 ticks (0.20 seconds real time)** — a 500% timing distortion.
2. **CRITICAL DEFECT (Pedagogy & State Machine)**: `RuleEvaluator.EvaluateExerciseBoundary` has **zero hysteresis and zero debounce**. When a vehicle oscillates across the perimeter of the reverse corridor ($X = 3.0\,\text{m}$) due to steering jitter at 50 Hz, every outward tick triggers a new fatal violation ($100\,\text{points}$). Over 50 ticks of jitter, the engine generates **25 separate fatal disqualifications and 2,500 penalty points**, completely corrupting the debrief log.
3. **CRITICAL DEFECT (Hardware Safety & FFB)**: `LogitechG27Adapter.SetNormalizedTorque` **completely bypasses the slew rate limiter**. It directly assigns `CurrentAppliedTorque = TargetTorque;` (Line 337). A step target change from $-1.0 \to +1.0$ executes instantaneously with an infinite slew rate ($\Delta = 2.0 / 0\,\text{s}$), defeating the ADR-006 rate limiter watchdog designed to protect the driver's wrists and motor gears.
4. **HIGH DEFECT (Numerical Stability)**: `FloatingOrigin.CheckAndShift` contains no NaN guards. Under IEEE 754 rules, `(NaN < threshold^2)` evaluates to `False`. The method does not abort, but executes `Math.Round(NaN / 256) * 256 = NaN`, permanently overwriting `CurrentOrigin` with `(NaN, 0, NaN)`. All subsequent spatial conversions and rendering transforms irreversibly collapse to NaN.
5. **HIGH DEFECT (IEEE 754 NaN Propagation)**: `DrivetrainMath.AxleTorque` and `ClutchTorque` fail to check for `double.IsNaN`. Under IEEE 754 logic, `(NaN <= 0)` and `(NaN > 1)` are both false, allowing `finalDrive = NaN` and `pedal = NaN` to bypass validation guards and propagate unhandled NaN values through the physics pipeline without throwing `ArgumentOutOfRangeException`.
6. **MEDIUM VULNERABILITY (Hardware Disconnection)**: `LogitechG27Adapter.IsConnected` is a passive auto-property. Setting `IsConnected = false` during active force feedback delivery fails to call `Stop()`, leaving stale non-zero torque active in `CurrentAppliedTorque`.
7. **MEDIUM VULNERABILITY (Geometric Fidelity)**: `RuleEvaluator.EvaluateExerciseBoundary` evaluates the vehicle as an infinitesimally small single point $(x, z)$, completely ignoring the sedan's physical footprint ($L = 4.50\,\text{m}$, mirror width $W = 2.25\,\text{m}$). A vehicle centered at $X = 2.50\,\text{m}$ in a $3.0\,\text{m}$ corridor has its right mirror protruding $62.5\,\text{cm}$ into the barriers, yet the evaluator reports a 100% clean pass without infraction.

---

## 1. Quantitative Test Results

All 26 empirical stress tests were executed using Python 3.13 / Pytest in `tests/test_adversarial_challenger1.py`, combined with full regression audit against the 134 existing project tests:

| Test ID | Subsystem | Adversarial Scenario | Outcome | Quantitative Result |
|---|---|---|---|---|
| `test_adv_drivetrain_nan_inputs_propagation_defect` | Drivetrain | Inject NaN to `final_drive`, `efficiency`, `pedal` | PASSED (Detected) | IEEE 754 comparison bypass; returns NaN without exception |
| `test_adv_drivetrain_infinity_inputs_propagation` | Drivetrain | Inject $+ \infty$ to `final_drive` | PASSED (Detected) | Bypasses `finalDrive <= 0`; returns $+\infty$ torque |
| `test_adv_drivetrain_negative_rpm_unhandled` | Drivetrain | Inject negative engine RPM ($-1000\,\text{rad/s}$) | PASSED (Detected) | Accepted without stall or exception; returns $-350\,\text{Nm}$ |
| `test_adv_drivetrain_instantaneous_redline_clutch_drop` | Drivetrain | Instantaneous clutch drop at 6500 RPM | PASSED | Slip $34,034\,\text{Nm}$ strictly clamped to capacity $350.0\,\text{Nm}$ |
| `test_adv_drivetrain_reverse_shift_at_150kph_stress` | Drivetrain | Reverse shift at $+150\,\text{km/h}$ ($41.67\,\text{m/s}$) | PASSED | Shaft $-1928.6\,\text{rad/s}$, axle torque $-4520.25\,\text{Nm}$ |
| `test_adv_driver_command_extreme_inputs_validation` | Contracts | Fuzzing axes with NaN, $\pm\infty$, out-of-range gears | PASSED | All illegal inputs rejected with `ValueError` |
| `test_adv_g27_centering_spring_at_300kph` | Logitech FFB | Centering spring at $300\,\text{km/h}$ ($83.33\,\text{m/s}$) | PASSED | Smoothly saturates at maximum stiffness $0.65$ ($\pm 0.65$) |
| `test_adv_g27_centering_spring_nan_speed_defect` | Logitech FFB | Centering spring with `speedMps = NaN` | PASSED (Detected) | `Math.Clamp(NaN)` returns NaN; spring force outputs NaN |
| `test_adv_g27_end_stop_violent_collisions` | Logitech FFB | Wheel angle overshoots past $450^\circ$ to $10,000^\circ$ | PASSED | Repulsive torque strictly clamped in $[-1.0, +1.0]$ |
| `test_adv_g27_high_frequency_100hz_steering_oscillation` | Logitech FFB | Sinusoidal steering at 100 Hz over 1,000 ticks | PASSED | Peak $\omega = 2467\,\text{rad/s}$; total torque bounded in $[-1.0, 1.0]$ |
| `test_adv_g27_rate_limiter_bounds` | Logitech FFB | Step jump $-1.0 \to +1.0$ at $10.0/\text{s}$ slew rate | PASSED | Traverses $2.0$ delta in exactly 20 ticks ($0.20\,\text{s}$) at $100\,\text{Hz}$ |
| `test_adv_g27_set_normalized_torque_bypasses_rate_limiter_defect` | Logitech FFB | Instantaneous torque reversal via `SetNormalizedTorque` | PASSED (Detected) | Directly sets `CurrentAppliedTorque`, bypassing slew limiter |
| `test_adv_g27_disconnect_stale_torque_vulnerability` | Logitech FFB | Sudden disconnect during active $0.8$ torque | PASSED (Detected) | `CurrentAppliedTorque` remains stale at $0.8$; no auto-zero |
| `test_adv_g27_rapid_pause_resume_cycles` | Logitech FFB | 1,000 rapid Pause/Resume cycles | PASSED | 100% zeroed on pause, clean recovery on resume, 0 drift |
| `test_adv_floating_origin_coordinates_at_limits` | Floating Origin | Limits at $\pm 10\,\text{km}$, $\pm 100\,\text{km}$, $\pm 1000\,\text{km}$ | PASSED | Snaps to integer multiples of 256m; $|x_{\text{local}}| \le 128\,\text{m}$ |
| `test_adv_floating_origin_submillimeter_precision_roundtrip` | Floating Origin | Canonical roundtrip at $100,000.123456\,\text{m}$ | PASSED | Max roundtrip error $< 0.0001\,\text{m}$ ($0.08\,\text{mm}$) |
| `test_adv_floating_origin_repeated_boundary_crossings_hysteresis` | Floating Origin | Oscillating $499\,\text{m} \leftrightarrow 501\,\text{m}$ | PASSED | Snaps to $512\,\text{m}$ at 501; returning to 499 does not thrash |
| `test_adv_floating_origin_velocity_invariance` | Floating Origin | $(v_{\text{local}} + \Delta_{\text{shift}})/\Delta t$ across 500m shift | PASSED | Conserved with machine precision (relative tolerance $< 10^{-5}$) |
| `test_adv_floating_origin_nan_corruption_defect` | Floating Origin | Inject NaN to `focusWorldX` | PASSED (Detected) | `CurrentOrigin` corrupted into `(NaN, 0, NaN)` |
| `test_adv_floating_origin_small_threshold_infinite_shift_loop_defect` | Floating Origin | `threshold = 100m < chunk = 256m` at $105\,\text{m}$ | PASSED (Detected) | Continuous zero-delta shift loop on every frame |
| `test_adv_floating_origin_vertical_elevation_blindness` | Floating Origin | Vehicle climbing to $Y = 10,000\,\text{m}$ | PASSED (Detected) | Origin strictly ignores Y; `CurrentOrigin.y` fixed at $0.0$ |
| `test_adv_rule_evaluator_stopline_tickrate_dependence_defect` | Rule Evaluator | Stop line evaluation at 100 Hz vs 20 Hz | PASSED (Detected) | Line 193 hardcodes 0.05s; 1.0s stop met in 0.20s real time |
| `test_adv_rule_evaluator_zero_speed_crossing` | Rule Evaluator | Vehicle creeping across stop line at $0.05\,\text{m/s}$ | PASSED | Unfulfilled stop correctly triggers violation |
| `test_adv_rule_evaluator_reverse_corridor_boundary_jitter_cascading_defect` | Rule Evaluator | 50 Hz boundary oscillation across $X = 3.0\,\text{m}$ | PASSED (Detected) | 25 fatal violations and 2,500 penalty points in 1 second |
| `test_adv_rule_evaluator_boundary_edge_exact_coordinates` | Rule Evaluator | Positions on exact perimeter ($X=0, 3$; $Z=0, 25$) | PASSED | Inclusive boundary checks treat edges as inside |
| `test_adv_rule_evaluator_point_vs_sedan_footprint_vulnerability` | Rule Evaluator | Car at $X=2.5\,\text{m}$ in $3.0\,\text{m}$ corridor | PASSED (Detected) | Mirror penetrates $62.5\,\text{cm}$ outside while evaluator passes |

**Execution Summary**:
- Challenger 1 Adversarial Suite: **26 passed in 0.13s** (Exit code 0).
- Total Project Regression Suite: **160 passed in 0.40s** (Exit code 0).
- Unity 6000.3.10f1 EditMode Suite: **53 passed, 0 failed, 0 errors** (Exit code 0).

---

## 2. Detailed Defect & Vulnerability Reports

### Defect 1 (CRITICAL): Stop Line Evaluation Hardcodes Tick Time Delta (`+= 0.05`)
- **Severity**: CRITICAL
- **Location**: `Assets/DrivingSchool/Code/Rules/RuleEvaluator.cs`, Line 193
- **Verbatim Code**:
  ```csharp
  if (isInStopZone && isStationary)
  {
      stopDurationAccumulated += 0.05; // Simulation tick increment or elapsed accumulation
      if (stopDurationAccumulated >= requiredStopSeconds)
      {
          stopLineCompleted = true;
      }
  }
  ```
- **Physical Analysis**:
  - The simulation architecture specifies a 100 Hz fixed physics tick rate ($dt = 0.01\,\text{s}$, ADR-005).
  - `EvaluateStopLine` accepts `double simSeconds` as its first parameter, but ignores it during duration accumulation.
  - At 100 Hz, in 1 elapsed second of real simulation time, `EvaluateStopLine` is invoked 100 times.
  - `stopDurationAccumulated` increases by $100 \times 0.05 = 5.0\,\text{seconds}$ every real second!
  - Consequently, a student stopping at a red light or stop line only needs to hold stationary for **20 ticks ($0.20\,\text{seconds}$)** to satisfy a mandatory 1.0-second stop requirement.
  - Conversely, if the simulator drops to 10 Hz during heavy rendering, a 1.0-second stop requires 2.0 seconds of real time.
- **Required Mitigation**:
  Track `prevSimSeconds` or pass `dtSeconds` explicitly:
  ```csharp
  double dt = Math.Max(0.0, simSeconds - prevSimSeconds);
  stopDurationAccumulated += dt;
  ```

---

### Defect 2 (CRITICAL): Exercise Boundary Lacks Hysteresis & Debounce, Causing Penalty Cascades
- **Severity**: CRITICAL
- **Location**: `Assets/DrivingSchool/Code/Rules/RuleEvaluator.cs`, Lines 251–277
- **Verbatim Code**:
  ```csharp
  if (!isInside && !boundaryViolationActive)
  {
      boundaryViolationActive = true;
      violation = new RuleEvent { ... penalty = TrafficRules.PenaltyFatal };
      AddInfraction(violation);
      return true;
  }
  if (isInside)
  {
      boundaryViolationActive = false;
  }
  ```
- **Physical Analysis**:
  - Autodrome exercises (Slalom, Reverse Corridor, Box Parking) have tight clearance boundaries (e.g. $3.0\,\text{m}$ corridor width).
  - When a student drives near the boundary, physical tire/chassis simulation produces micro-oscillations across the boundary line.
  - Because `boundaryViolationActive` resets to `false` the microsecond the position re-enters the boundary, every oscillation outside registers as a brand new fatal infraction.
  - In `test_adv_rule_evaluator_reverse_corridor_boundary_jitter_cascading_defect`, 50 ticks of jitter across $X = 3.0\,\text{m}$ generated **25 separate fatal infractions and 2,500 penalty points in 1 second**.
- **Required Mitigation**:
  Add boundary hysteresis margin (e.g. require returning $10\,\text{cm}$ inside the boundary to reset) and a debounce cooldown window (minimum 1.0s between boundary events).

---

### Defect 3 (CRITICAL): `LogitechG27Adapter.SetNormalizedTorque` Bypasses Slew Rate Limiter
- **Severity**: CRITICAL
- **Location**: `Assets/DrivingSchool/Code/Input/LogitechG27Adapter.cs`, Lines 335–337
- **Verbatim Code**:
  ```csharp
  TargetTorque = Math.Clamp(torque, -1f, 1f);
  // In instantaneous set, apply clamped value
  CurrentAppliedTorque = TargetTorque;
  ```
- **Physical Analysis**:
  - ADR-006 and PROJECT.md §F46 explicitly mandate an FFB rate limiter bounded to $10.0/\text{s}$ to prevent violent wheel kickback and motor damage.
  - While `ApplyRateLimiter` is correctly implemented in `LogitechG27Adapter.UpdateTorque`, the primary interface method `IForceFeedbackOutput.SetNormalizedTorque` completely ignores the rate limiter and writes directly to `CurrentAppliedTorque`.
  - When game logic or safety layers call `SetNormalizedTorque`, the motor torque can swing from $-1.0 \to +1.0$ in zero seconds, generating an infinite slew rate and risking physical injury on direct-drive and geared wheels.
- **Required Mitigation**:
  Route all torque modifications through `ApplyRateLimiter` or maintain an internal timestamp to compute elapsed `dt` inside `SetNormalizedTorque`.

---

### Defect 4 (HIGH): `FloatingOrigin.CheckAndShift` Corrupted Permanently by NaN Coordinates
- **Severity**: HIGH
- **Location**: `Assets/DrivingSchool/Code/World/FloatingOrigin.cs`, Lines 135–150
- **Verbatim Code**:
  ```csharp
  double dx = focusWorldX - CurrentOrigin.x;
  double dz = focusWorldZ - CurrentOrigin.z;
  double distSqr = dx * dx + dz * dz;

  if (distSqr < ShiftThresholdM * ShiftThresholdM) return false;

  double newOriginX = Math.Round(focusWorldX / ChunkSizeM) * ChunkSizeM;
  ```
- **Physical Analysis**:
  - When `focusWorldX` or `focusWorldZ` is `NaN`, `distSqr` is `NaN`.
  - In IEEE 754 floating-point arithmetic, `(NaN < ShiftThresholdM * ShiftThresholdM)` evaluates to `False`.
  - The check fails to abort; instead, execution continues into `Math.Round(NaN / ChunkSizeM)`, resulting in `NaN`.
  - `CurrentOrigin` is assigned `Vector3d(NaN, 0, NaN)`.
  - Once `CurrentOrigin` is corrupted, all subsequent calls to `ToLocal` and `ToWorld` return NaN, permanently breaking world rendering and spatial streaming.
- **Required Mitigation**:
  Add an explicit finite sanity check at the start of `CheckAndShift`:
  ```csharp
  if (double.IsNaN(focusWorldX) || double.IsInfinity(focusWorldX) ||
      double.IsNaN(focusWorldZ) || double.IsInfinity(focusWorldZ))
      return false;
  ```

---

### Defect 5 (HIGH): `DrivetrainMath` IEEE 754 NaN Comparison Vulnerability
- **Severity**: HIGH
- **Location**: `Assets/DrivingSchool/Code/Simulation/DrivetrainMath.cs`, Lines 7–17
- **Verbatim Code**:
  ```csharp
  public static double AxleTorque(double clutchTorqueNm, double gearRatio, double finalDrive, double efficiency)
  {
      if (finalDrive<=0||efficiency<0||efficiency>1) throw new ArgumentOutOfRangeException();
      return clutchTorqueNm*gearRatio*finalDrive*efficiency;
  }
  public static double ClutchTorque(double engineRadS, double inputShaftRadS, double pedal, double capacityNm, double coupling)
  {
      if (pedal<0||pedal>1||capacityNm<0||coupling<0) throw new ArgumentOutOfRangeException();
      ...
  }
  ```
- **Physical Analysis**:
  - Comparisons against `NaN` (`finalDrive <= 0`, `efficiency < 0`, `pedal < 0`, `pedal > 1`) all evaluate to `False`.
  - When NaN values enter `AxleTorque` or `ClutchTorque`, no exception is thrown; instead, NaN propagates directly into axle and clutch output forces.
  - Additionally, `finalDrive = +Infinity` does not satisfy `finalDrive <= 0` and produces infinite axle torque.
- **Required Mitigation**:
  Validate `!double.IsNaN(v) && !double.IsInfinity(v)` for all inputs.

---

### Defect 6 (MEDIUM): `LogitechG27Adapter` Leaks Stale Active Torque on Disconnect
- **Severity**: MEDIUM
- **Location**: `Assets/DrivingSchool/Code/Input/LogitechG27Adapter.cs`, Line 128
- **Verbatim Code**:
  ```csharp
  public bool IsConnected { get; set; } = true;
  ```
- **Analysis**:
  - `IsConnected` is a plain auto-property without setter side effects.
  - When hardware disconnects, setting `adapter.IsConnected = false` does not invoke `Stop()`.
  - `CurrentAppliedTorque` retains its prior non-zero value, risking stale force output when the hardware reconnects.
- **Required Mitigation**:
  Convert `IsConnected` to a backing property that invokes `Stop()` when transitioned to `false`.

---

### Defect 7 (MEDIUM): Point-Particle Collision Check Ignores 3D Sedan Footprint
- **Severity**: MEDIUM
- **Location**: `Assets/DrivingSchool/Code/Rules/RuleEvaluator.cs`, Lines 248–250
- **Analysis**:
  - `EvaluateExerciseBoundary` tests an infinitesimal mathematical point $(x, z)$.
  - The student sedan has physical dimensions: Length $4.50\,\text{m}$, Body Width $1.80\,\text{m}$, Mirror Span $2.25\,\text{m}$ (half-width $1.125\,\text{m}$).
  - When a student drives with the car's origin at $X = 2.50\,\text{m}$ in a $3.0\,\text{m}$ corridor, the right wing mirror protrudes $62.5\,\text{cm}$ into the barrier. The evaluator reports 0 infractions.
- **Required Mitigation**:
  Evaluate the 4 outer corner points or oriented bounding box (OBB) of the vehicle against exercise perimeters.

---

## 3. Recommended Remediation Plan

To transition this milestone to `APPROVE`:
1. **Rule Evaluator (`RuleEvaluator.cs`)**:
   - Replace hardcoded `stopDurationAccumulated += 0.05` with elapsed time delta `simSeconds - prevSimSeconds`.
   - Implement spatial hysteresis ($0.10\,\text{m}$) and cooldown debounce ($1.0\,\text{s}$) on `EvaluateExerciseBoundary`.
   - Incorporate vehicle half-width and half-length into boundary checks.
2. **Logitech G27 Adapter (`LogitechG27Adapter.cs`)**:
   - In `SetNormalizedTorque`, clamp input to `TargetTorque` and apply `ApplyRateLimiter` rather than setting `CurrentAppliedTorque` instantaneously.
   - Guard `CalculateCenteringSpring` against `speedMps = NaN`.
   - Update `IsConnected` setter to call `Stop()` when set to `false`.
3. **Floating Origin (`FloatingOrigin.cs`)**:
   - Add finite sanity checks for `focusWorldX` and `focusWorldZ` in `CheckAndShift`.
   - Ensure `ShiftThresholdM >= ChunkSizeM * 0.5` to prevent zero-delta infinite loops.
4. **Drivetrain Math (`DrivetrainMath.cs`)**:
   - Add finite validation (`!double.IsNaN` / `!double.IsInfinity`) across all calculation inputs.
   - Clamp negative engine angular velocity to zero.
5. **Re-run Adversarial Test Suite**:
   - Verify all 26 tests in `tests/test_adversarial_challenger1.py` and 18 tests in `tests/test_adversarial_challenger2.py` pass cleanly.

"""Adversarial Stress Test Suite - Phase 1 Challenger 1.

Target Subsystems:
1. Drivetrain & Physics:
   - DrivetrainMath & DriverCommand: Extreme inputs, negative RPM, NaN, Infinity,
     instantaneous redline clutching, reverse gear shifts at 150 km/h, IEEE 754 NaN propagation.
2. Logitech G27 FFB & Adapter:
   - Centering spring at 300 km/h, mechanical stop collisions (> 450 deg), violent 100 Hz
     steering oscillation, rate limiter bounds, rate limiter bypass in SetNormalizedTorque,
     stale torque on sudden disconnect, rapid pause/resume cycles.
3. Floating Origin:
   - Coordinate limits (±10,000m, ±100,000m, ±1,000,000m), repeated 500m threshold crossings,
     velocity invariance, sub-millimeter precision roundtrips, NaN corruption, zero-delta shift loop,
     vertical Y blindness.
4. Rule Evaluator:
   - Stop line tick-rate dependence (hardcoded 0.05s dt), zero-speed crossing,
     reverse corridor boundary jitter (cascading fatal penalties due to zero hysteresis),
     point-particle vs sedan 3D footprint blindness.
"""
from __future__ import annotations
import math
import pytest
from dataclasses import dataclass, field
from typing import List, Optional, Tuple

from tests.sim_contracts import (
    DriverCommand,
    VehicleState,
    DrivetrainMath,
    RuleEvent
)

# ==============================================================================
# REFERENCE PYTHON MIRRORS OF C# SUBSYSTEMS (Line-for-line matching C# logic)
# ==============================================================================

class Vector3d:
    def __init__(self, x: float = 0.0, y: float = 0.0, z: float = 0.0):
        self.x = float(x)
        self.y = float(y)
        self.z = float(z)

    def __eq__(self, other):
        if not isinstance(other, Vector3d):
            return False
        return self.x == other.x and self.y == other.y and self.z == other.z

    def __repr__(self):
        return f"Vector3d({self.x:.3f}, {self.y:.3f}, {self.z:.3f})"


class FloatingOriginMirror:
    """Line-for-line mirror of Assets/DrivingSchool/Code/World/FloatingOrigin.cs."""
    DEFAULT_CHUNK_SIZE_M = 256
    DEFAULT_SHIFT_THRESHOLD_M = 500.0

    def __init__(self, chunk_size_m: int = DEFAULT_CHUNK_SIZE_M, shift_threshold_m: float = DEFAULT_SHIFT_THRESHOLD_M):
        self.chunk_size_m = chunk_size_m if chunk_size_m > 0 else self.DEFAULT_CHUNK_SIZE_M
        self.shift_threshold_m = shift_threshold_m if shift_threshold_m > 0.0 else self.DEFAULT_SHIFT_THRESHOLD_M
        self.current_origin = Vector3d(0.0, 0.0, 0.0)
        self.shift_count = 0
        self.shift_history: List[Tuple[Vector3d, Vector3d, Tuple[float, float, float]]] = []

    def set_origin(self, x: float, y: float, z: float):
        self.current_origin = Vector3d(x, y, z)

    def to_local(self, world_x: float, world_y: float, world_z: float) -> Tuple[float, float, float]:
        # C# cast to single-precision float: (float)(worldX - CurrentOrigin.x)
        # Using struct pack/unpack or single precision emulation
        import struct
        def to_f32(val: float) -> float:
            return struct.unpack('f', struct.pack('f', float(val)))[0]

        lx = to_f32(world_x - self.current_origin.x)
        ly = to_f32(world_y - self.current_origin.y)
        lz = to_f32(world_z - self.current_origin.z)
        return (lx, ly, lz)

    def to_world(self, local_x: float, local_y: float, local_z: float) -> Vector3d:
        return Vector3d(
            self.current_origin.x + local_x,
            self.current_origin.y + local_y,
            self.current_origin.z + local_z
        )

    def check_and_shift(self, focus_world_x: float, focus_world_y: float, focus_world_z: float) -> Tuple[bool, Tuple[float, float, float]]:
        dx = focus_world_x - self.current_origin.x
        dz = focus_world_z - self.current_origin.z
        dist_sqr = dx * dx + dz * dz

        # Note: in C# and Python, if dx or dz is NaN, dist_sqr is NaN and (NaN < threshold^2) is False
        if dist_sqr < self.shift_threshold_m * self.shift_threshold_m:
            return False, (0.0, 0.0, 0.0)

        # Snap new origin to integer multiple of chunk size (256m grid)
        # Note: Math.Round(double.NaN) in C# returns NaN without throwing.
        if math.isnan(focus_world_x) or math.isnan(focus_world_z):
            new_origin_x = float('nan')
            new_origin_z = float('nan')
        else:
            new_origin_x = round(focus_world_x / self.chunk_size_m) * self.chunk_size_m
            new_origin_z = round(focus_world_z / self.chunk_size_m) * self.chunk_size_m
        new_origin_y = 0.0

        old_origin = Vector3d(self.current_origin.x, self.current_origin.y, self.current_origin.z)
        new_origin = Vector3d(new_origin_x, new_origin_y, new_origin_z)

        shift_delta = (
            float(new_origin.x - old_origin.x),
            float(new_origin.y - old_origin.y),
            float(new_origin.z - old_origin.z)
        )

        self.current_origin = new_origin
        self.shift_count += 1
        self.shift_history.append((old_origin, new_origin, shift_delta))
        return True, shift_delta


class LogitechG27AdapterMirror:
    """Line-for-line mirror of Assets/DrivingSchool/Code/Input/LogitechG27Adapter.cs."""
    MAX_STEERING_ANGLE_DEG = 450.0
    DEFAULT_MAX_MOTOR_TORQUE_NM = 2.5
    DEFAULT_MAX_SLEW_RATE = 10.0

    def __init__(self):
        self.max_motor_torque_nm = self.DEFAULT_MAX_MOTOR_TORQUE_NM
        self.max_slew_rate = self.DEFAULT_MAX_SLEW_RATE
        self.current_applied_torque = 0.0
        self.target_torque = 0.0
        self.is_paused = False
        self.is_stopped = False
        self.disposed = False
        self.is_connected = True

    @property
    def is_available(self) -> bool:
        return self.is_connected and not self.disposed and not self.is_stopped and not self.is_paused

    @staticmethod
    def clamp(v: float, min_v: float, max_v: float) -> float:
        if math.isnan(v):
            return float('nan')
        return max(min_v, min(max_v, v))

    @classmethod
    def calculate_centering_spring(cls, steering_norm: float, speed_mps: float, base_stiffness: float = 0.15, max_stiffness: float = 0.65) -> float:
        if math.isnan(steering_norm) or math.isinf(steering_norm):
            return 0.0
        # In C#: float speedFactor = Math.Clamp(Math.Abs(speedMps) / 15.0f, 0f, 1f);
        if math.isnan(speed_mps):
            # Math.Clamp in C# returns NaN when value is NaN!
            speed_factor = float('nan')
        else:
            speed_factor = cls.clamp(abs(speed_mps) / 15.0, 0.0, 1.0)
        k_spring = base_stiffness + (max_stiffness - base_stiffness) * speed_factor
        return -steering_norm * k_spring

    @classmethod
    def calculate_damping(cls, steering_angular_velocity_rad_s: float, damping_coeff: float = 0.12) -> float:
        if math.isnan(steering_angular_velocity_rad_s) or math.isinf(steering_angular_velocity_rad_s):
            return 0.0
        return -steering_angular_velocity_rad_s * damping_coeff

    @classmethod
    def calculate_friction(cls, steering_angular_velocity_rad_s: float, friction_torque: float = 0.05) -> float:
        if math.isnan(steering_angular_velocity_rad_s) or math.isinf(steering_angular_velocity_rad_s):
            return 0.0
        if abs(steering_angular_velocity_rad_s) < 0.01:
            return 0.0
        return -math.copysign(friction_torque, steering_angular_velocity_rad_s)

    @classmethod
    def calculate_end_stop(cls, wheel_angle_deg: float, max_angle_deg: float = 450.0, stop_stiffness: float = 0.10) -> float:
        if math.isnan(wheel_angle_deg) or math.isinf(wheel_angle_deg):
            return 0.0
        abs_angle = abs(wheel_angle_deg)
        if abs_angle <= max_angle_deg:
            return 0.0
        penetration = abs_angle - max_angle_deg
        stop_force = cls.clamp(penetration * stop_stiffness, 0.0, 1.0)
        return -math.copysign(stop_force, wheel_angle_deg)

    @classmethod
    def apply_rate_limiter(cls, target_torque: float, current_torque: float, dt_seconds: float, max_slew_rate: float = 10.0) -> float:
        if math.isnan(target_torque) or math.isinf(target_torque):
            return 0.0
        if math.isnan(current_torque) or math.isinf(current_torque):
            current_torque = 0.0
        if dt_seconds <= 0.0:
            return current_torque

        max_delta = max_slew_rate * dt_seconds
        delta = target_torque - current_torque
        clamped_delta = cls.clamp(delta, -max_delta, max_delta)
        return cls.clamp(current_torque + clamped_delta, -1.0, 1.0)

    def calculate_total_torque(self, self_aligning_torque_nm: float, steering_norm: float,
                               steering_angular_velocity_rad_s: float, speed_mps: float,
                               front_slip_angle_rad: float, sim_seconds: float) -> float:
        if not self.is_available:
            return 0.0
        norm_aligning = (self_aligning_torque_nm / self.max_motor_torque_nm) if self.max_motor_torque_nm > 0 else 0.0
        centering = self.calculate_centering_spring(steering_norm, speed_mps)
        damping = self.calculate_damping(steering_angular_velocity_rad_s)
        friction = self.calculate_friction(steering_angular_velocity_rad_s)
        end_stop = self.calculate_end_stop(steering_norm * self.MAX_STEERING_ANGLE_DEG, self.MAX_STEERING_ANGLE_DEG)
        
        # Grip loss vibration
        grip_loss = 0.0
        if abs(speed_mps) >= 1.0 and not math.isnan(front_slip_angle_rad) and not math.isinf(front_slip_angle_rad):
            abs_slip = abs(front_slip_angle_rad)
            if abs_slip > 0.09:
                excess = self.clamp((abs_slip - 0.09) / 0.10, 0.0, 1.0)
                sine = math.sin(2.0 * math.pi * 22.0 * sim_seconds)
                grip_loss = sine * 0.25 * excess

        total = norm_aligning + centering + damping + friction + end_stop + grip_loss
        if math.isnan(total) or math.isinf(total):
            return 0.0
        return self.clamp(total, -1.0, 1.0)

    def set_normalized_torque(self, torque: float):
        """C# SetNormalizedTorque implementation (note: bypasses rate limiter)."""
        if not self.is_available:
            self.target_torque = 0.0
            self.current_applied_torque = 0.0
            return
        if math.isnan(torque) or math.isinf(torque):
            self.stop()
            return
        self.target_torque = self.clamp(torque, -1.0, 1.0)
        self.current_applied_torque = self.target_torque

    def update_torque(self, target_torque: float, dt_seconds: float):
        if not self.is_available:
            self.current_applied_torque = 0.0
            self.target_torque = 0.0
            return
        self.target_torque = self.clamp(target_torque, -1.0, 1.0)
        self.current_applied_torque = self.apply_rate_limiter(self.target_torque, self.current_applied_torque, dt_seconds, self.max_slew_rate)

    def pause(self):
        self.is_paused = True
        self.stop()

    def resume(self):
        self.is_paused = False
        self.is_stopped = False

    def stop(self):
        self.is_stopped = True
        self.target_torque = 0.0
        self.current_applied_torque = 0.0

    def dispose(self):
        if not self.disposed:
            self.stop()
            self.disposed = True


class RuleEvaluatorMirror:
    """Line-for-line mirror of Assets/DrivingSchool/Code/Rules/RuleEvaluator.cs."""
    RULE_SPEED_LIMIT = "PDD_10.2_SPEED_LIMIT"
    RULE_STOP_LINE = "PDD_6.13_STOP_LINE"
    RULE_EXERCISE_BOUNDARY = "AUTODROME_EXERCISE_BOUNDARY"
    RULE_TURN_SIGNAL = "PDD_8.1_TURN_SIGNAL"

    PENALTY_TURN_SIGNAL = 5
    PENALTY_SPEED_MINOR = 15
    PENALTY_SPEED_MAJOR = 25
    PENALTY_STOP_LINE_OVERRUN = 25
    PENALTY_FATAL = 100

    def __init__(self):
        self.infractions: List[RuleEvent] = []
        self.total_penalty_points = 0
        self.has_fatal_violation = False

        self.speeding_start_time = -1.0
        self.speed_violation_active = False

        self.stop_duration_accumulated = 0.0
        self.stop_line_completed = False
        self.stop_line_violation_active = False

        self.boundary_violation_active = False
        self.turn_signal_violation_active = False

    def reset(self):
        self.infractions.clear()
        self.total_penalty_points = 0
        self.has_fatal_violation = False
        self.speeding_start_time = -1.0
        self.speed_violation_active = False
        self.stop_duration_accumulated = 0.0
        self.stop_line_completed = False
        self.stop_line_violation_active = False
        self.boundary_violation_active = False
        self.turn_signal_violation_active = False

    def add_infraction(self, ev: RuleEvent):
        if ev is None:
            return
        self.infractions.append(ev)
        self.total_penalty_points += ev.penalty
        if ev.penalty >= self.PENALTY_FATAL or ev.rule_id == self.RULE_EXERCISE_BOUNDARY:
            self.has_fatal_violation = True

    def evaluate_speed_limit(self, sim_seconds: float, x: float, y: float, z: float,
                             speed_mps: float, speed_limit_kph: float, grace_kph: float = 5.0,
                             debounce_seconds: float = 1.5) -> Tuple[bool, Optional[RuleEvent]]:
        if speed_limit_kph <= 0.0 or math.isnan(speed_mps) or math.isinf(speed_mps):
            return False, None

        current_speed_kph = abs(speed_mps) * 3.6
        threshold_kph = speed_limit_kph + grace_kph
        is_over = current_speed_kph > threshold_kph

        if is_over:
            if self.speeding_start_time < 0.0:
                self.speeding_start_time = sim_seconds

            duration = sim_seconds - self.speeding_start_time
            if duration >= debounce_seconds and not self.speed_violation_active:
                self.speed_violation_active = True
                excess = current_speed_kph - speed_limit_kph
                penalty = self.PENALTY_SPEED_MAJOR if excess > 20.0 else self.PENALTY_SPEED_MINOR
                ev = RuleEvent(
                    id="speed_event",
                    rule_id=self.RULE_SPEED_LIMIT,
                    rule_revision="2026.01",
                    participant_id="player",
                    evidence_id=f"speed-{current_speed_kph:.1f}",
                    explanation_key="rule.speed_limit_exceeded",
                    simulation_seconds=sim_seconds,
                    x=x, y=y, z=z,
                    penalty=penalty
                )
                self.add_infraction(ev)
                return True, ev
        else:
            self.speeding_start_time = -1.0
            if current_speed_kph < (threshold_kph - 2.0):
                self.speed_violation_active = False

        return False, None

    def evaluate_stop_line(self, sim_seconds: float, x: float, y: float, z: float,
                           speed_mps: float, distance_to_stop_line: float, is_past_stop_line: bool,
                           signal_requires_stop: bool, stop_tolerance_m: float = 1.5,
                           required_stop_seconds: float = 1.0) -> Tuple[bool, Optional[RuleEvent]]:
        """Exact mirror of C# RuleEvaluator.EvaluateStopLine (including line 193 hardcoded 0.05s)."""
        if not signal_requires_stop:
            self.stop_duration_accumulated = 0.0
            self.stop_line_completed = False
            self.stop_line_violation_active = False
            return False, None

        is_stationary = abs(speed_mps) <= 0.14
        is_in_stop_zone = -0.5 <= distance_to_stop_line <= stop_tolerance_m

        if is_in_stop_zone and is_stationary:
            # Line 193 in C#: stopDurationAccumulated += 0.05;
            self.stop_duration_accumulated += 0.05
            if self.stop_duration_accumulated >= required_stop_seconds:
                self.stop_line_completed = True

        if is_past_stop_line and not self.stop_line_completed and not self.stop_line_violation_active:
            self.stop_line_violation_active = True
            ev = RuleEvent(
                id="stopline_event",
                rule_id=self.RULE_STOP_LINE,
                rule_revision="2026.01",
                participant_id="player",
                evidence_id=f"stopline-dist-{distance_to_stop_line:.2f}",
                explanation_key="rule.stop_line_overrun",
                simulation_seconds=sim_seconds,
                x=x, y=y, z=z,
                penalty=self.PENALTY_STOP_LINE_OVERRUN
            )
            self.add_infraction(ev)
            return True, ev

        return False, None

    def evaluate_exercise_boundary(self, sim_seconds: float, x: float, y: float, z: float,
                                   min_x: float, max_x: float, min_z: float, max_z: float,
                                   exercise_id: str = "exercise") -> Tuple[bool, Optional[RuleEvent]]:
        """Exact mirror of C# RuleEvaluator.EvaluateExerciseBoundary."""
        low_x = min(min_x, max_x)
        high_x = max(min_x, max_x)
        low_z = min(min_z, max_z)
        high_z = max(min_z, max_z)

        is_inside = (low_x <= x <= high_x) and (low_z <= z <= high_z)

        if not is_inside and not self.boundary_violation_active:
            self.boundary_violation_active = True
            ev = RuleEvent(
                id="boundary_event",
                rule_id=self.RULE_EXERCISE_BOUNDARY,
                rule_revision="2026.01",
                participant_id="player",
                evidence_id=f"boundary-out-{exercise_id}",
                explanation_key="rule.exercise_boundary_crossed",
                simulation_seconds=sim_seconds,
                x=x, y=y, z=z,
                penalty=self.PENALTY_FATAL
            )
            self.add_infraction(ev)
            return True, ev

        if is_inside:
            self.boundary_violation_active = False

        return False, None


# ==============================================================================
# 1. DRIVETRAIN & PHYSICS TESTS
# ==============================================================================

def test_adv_drivetrain_nan_inputs_propagation_defect():
    """VULNERABILITY AUDIT: Passing NaN to DrivetrainMath.axle_torque and clutch_torque.
    
    In IEEE 754: (NaN <= 0) is False, (NaN < 0) is False, (NaN > 1) is False.
    Both C# and Python implementations fail to check for NaN, allowing NaN to bypass range
    guards and propagate corrupted NaN values without raising an error.
    """
    # Test axle_torque with NaN final_drive: NaN <= 0 is False, so no ValueError raised!
    val = DrivetrainMath.axle_torque(100.0, 3.5, float('nan'), 0.9)
    assert math.isnan(val), "axle_torque with NaN final_drive propagated NaN silently"

    # Test axle_torque with NaN efficiency: NaN < 0 is False, NaN > 1 is False
    val_eff = DrivetrainMath.axle_torque(100.0, 3.5, 4.1, float('nan'))
    assert math.isnan(val_eff), "axle_torque with NaN efficiency propagated NaN silently"

    # Test clutch_torque with NaN pedal: NaN < 0 is False, NaN > 1 is False
    val_clutch = DrivetrainMath.clutch_torque(100.0, 50.0, float('nan'), 350.0, 50.0)
    assert math.isnan(val_clutch), "clutch_torque with NaN pedal propagated NaN silently"


def test_adv_drivetrain_infinity_inputs_propagation():
    """Adversarial injection of Infinity into DrivetrainMath.
    final_drive = +Inf is not <= 0, so it bypasses validation in unhardened implementations.
    """
    # In unhardened axle_torque: Inf * 100 * 3.5 * 0.9 = Inf
    val = DrivetrainMath.axle_torque(100.0, 3.5, float('inf'), 0.9)
    assert math.isinf(val) and val > 0, "Infinity final drive produced infinite torque"


def test_adv_drivetrain_negative_rpm_unhandled():
    """Negative engine RPM injection: internal combustion engines cannot rotate backwards.
    DrivetrainMath does not clamp or validate negative engine speeds.
    """
    # Injecting -1000 rad/s (~ -9550 RPM)
    torque = DrivetrainMath.clutch_torque(engine_rad_s=-1000.0, input_shaft_rad_s=0.0, pedal=0.0, capacity_nm=350.0, coupling=50.0)
    # Clamped to capacity -350 Nm, but engine state is physically impossible
    assert torque == -350.0, "Clutch clamped negative torque but engine model accepted negative RPM"


def test_adv_drivetrain_instantaneous_redline_clutch_drop():
    """Stress test instantaneous clutch drop at engine redline (6500 RPM) with stationary vehicle.
    Engine: 6500 RPM (680.68 rad/s). Input shaft: 0 rad/s.
    Pedal instantaneously drops from 1.0 (disengaged) to 0.0 (engaged).
    Torque must clamp strictly to clutch capacity (350 Nm) without numerical overflow.
    """
    omega_engine = 6500.0 * (2.0 * math.pi / 60.0) # 680.678 rad/s
    omega_shaft = 0.0
    capacity_nm = 350.0
    coupling = 50.0

    # Disengaged: pedal = 1.0 -> torque must be 0.0
    torque_disengaged = DrivetrainMath.clutch_torque(omega_engine, omega_shaft, pedal=1.0, capacity_nm=capacity_nm, coupling=coupling)
    assert torque_disengaged == 0.0

    # Instant drop: pedal = 0.0
    raw_slip = (omega_engine - omega_shaft) * coupling # 34,033.9 Nm!
    torque_engaged = DrivetrainMath.clutch_torque(omega_engine, omega_shaft, pedal=0.0, capacity_nm=capacity_nm, coupling=coupling)
    assert torque_engaged == capacity_nm, f"Clutch torque must clamp to capacity {capacity_nm}, got {torque_engaged}"

    # Verify axle torque in 1st gear (ratio = 3.636, final drive = 4.1, eff = 0.9)
    gear_1_ratio = 3.636
    final_drive = 4.1
    eff = 0.9
    axle_torque = DrivetrainMath.axle_torque(torque_engaged, gear_1_ratio, final_drive, eff)
    expected_axle = 350.0 * 3.636 * 4.1 * 0.9 # 4699.548 Nm
    assert math.isclose(axle_torque, expected_axle, rel_tol=1e-4)


def test_adv_drivetrain_reverse_shift_at_150kph_stress():
    """Stress test shifting into reverse while cruising forward at 150 km/h (41.67 m/s).
    Reverse gear ratio = -3.5, final drive = 4.1, total ratio = -14.35.
    Input shaft angular speed: 134.4 rad/s * (-14.35) = -1928.6 rad/s (-18,417 RPM!).
    Engine at idle (850 RPM = 89.0 rad/s).
    Clutch engagement must generate reverse torque clamped to 350 Nm and axle torque -4520.25 Nm.
    """
    speed_mps = 150.0 / 3.6 # 41.667 m/s
    wheel_radius = 0.31 # meters
    omega_wheel = speed_mps / wheel_radius # 134.41 rad/s
    r_gear = -3.5
    final_drive = 4.1
    omega_shaft = omega_wheel * (r_gear * final_drive) # -1928.77 rad/s

    omega_engine = 850.0 * (2.0 * math.pi / 60.0) # 89.01 rad/s
    capacity_nm = 350.0
    coupling = 50.0

    # Clutch torque: delta is positive (engine - shaft = 89.01 - (-1928.77) = 2017.78 rad/s)
    clutch_torque = DrivetrainMath.clutch_torque(omega_engine, omega_shaft, pedal=0.0, capacity_nm=capacity_nm, coupling=coupling)
    assert clutch_torque == capacity_nm # clamped to +350 Nm

    # Axle torque: transmission ratio is negative, producing massive braking/reverse torque
    axle_torque = DrivetrainMath.axle_torque(clutch_torque, r_gear, final_drive, 0.9)
    expected_axle = 350.0 * (-3.5) * 4.1 * 0.9 # -4520.25 Nm
    assert math.isclose(axle_torque, expected_axle, rel_tol=1e-4)
    assert axle_torque < 0.0, "Axle torque must oppose forward motion"


def test_adv_driver_command_extreme_inputs_validation():
    """Adversarial fuzzing of DriverCommand.validate().
    Verifies that all non-finite, out-of-bounds axes and gears are strictly rejected.
    """
    cmd = DriverCommand()

    # Valid baseline
    cmd.validate()

    # Steering boundaries & NaN/Inf
    for bad_steer in (float('nan'), float('inf'), float('-inf'), 1.001, -1.001, 100.0, -50.0):
        cmd.steering = bad_steer
        with pytest.raises(ValueError, match="DriverCommand validation failed"):
            cmd.validate()
    cmd.steering = 0.0

    # Throttle boundaries & NaN/Inf
    for bad_throttle in (float('nan'), float('inf'), -0.001, 1.001, 2.0, -1.0):
        cmd.throttle = bad_throttle
        with pytest.raises(ValueError, match="DriverCommand validation failed"):
            cmd.validate()
    cmd.throttle = 0.0

    # Brake boundaries
    for bad_brake in (float('nan'), float('inf'), -0.001, 1.001):
        cmd.brake = bad_brake
        with pytest.raises(ValueError, match="DriverCommand validation failed"):
            cmd.validate()
    cmd.brake = 0.0

    # Clutch boundaries
    for bad_clutch in (float('nan'), float('inf'), -0.001, 1.001):
        cmd.clutch = bad_clutch
        with pytest.raises(ValueError, match="DriverCommand validation failed"):
            cmd.validate()
    cmd.clutch = 0.0

    # Gear range [-1 .. 6]
    for bad_gear in (-100, -2, 7, 8, 999):
        cmd.requested_gear = bad_gear
        with pytest.raises(ValueError, match="DriverCommand validation failed"):
            cmd.validate()

    for valid_gear in (-1, 0, 1, 2, 3, 4, 5, 6):
        cmd.requested_gear = valid_gear
        cmd.validate()


# ==============================================================================
# 2. LOGITECH G27 FFB & ADAPTER TESTS
# ==============================================================================

def test_adv_g27_centering_spring_at_300kph():
    """Centering spring stress at 300 km/h (83.33 m/s).
    Speed exceeds 15 m/s saturation threshold; spring stiffness must saturate smoothly at maxStiffness (0.65).
    """
    speed_300kph = 300.0 / 3.6 # 83.33 m/s
    torque_right = LogitechG27AdapterMirror.calculate_centering_spring(steering_norm=1.0, speed_mps=speed_300kph)
    assert torque_right == -0.65, f"Centering spring must saturate at -0.65, got {torque_right}"

    torque_left = LogitechG27AdapterMirror.calculate_centering_spring(steering_norm=-1.0, speed_mps=speed_300kph)
    assert torque_left == 0.65, f"Centering spring must saturate at +0.65, got {torque_left}"

    # Extreme supersonic speed (1000 m/s)
    torque_hypersonic = LogitechG27AdapterMirror.calculate_centering_spring(steering_norm=0.5, speed_mps=1000.0)
    assert torque_hypersonic == -0.325 # 0.5 * 0.65


def test_adv_g27_centering_spring_nan_speed_defect():
    """VULNERABILITY AUDIT: Centering spring calculation with NaN speed.
    In C# and Python, Math.Clamp(NaN, 0, 1) returns NaN, propagating NaN to spring torque!
    """
    torque_nan = LogitechG27AdapterMirror.calculate_centering_spring(steering_norm=0.5, speed_mps=float('nan'))
    assert math.isnan(torque_nan), "Centering spring calculation propagated NaN when speed is NaN"


def test_adv_g27_end_stop_violent_collisions():
    """Mechanical end stop bumpers under severe collisions (> 450 deg).
    Lock-to-lock is 900 deg (±450 deg).
    Tested up to 900 deg and 10,000 deg: repulsive force must stay strictly clamped in [-1.0, 1.0].
    """
    # Inside limits: no force
    assert LogitechG27AdapterMirror.calculate_end_stop(0.0) == 0.0
    assert LogitechG27AdapterMirror.calculate_end_stop(450.0) == 0.0
    assert LogitechG27AdapterMirror.calculate_end_stop(-450.0) == 0.0

    # Penetration: 460 deg (penetration 10 deg * 0.10 stiffness = 1.0 max)
    assert LogitechG27AdapterMirror.calculate_end_stop(460.0) == -1.0
    assert LogitechG27AdapterMirror.calculate_end_stop(-460.0) == 1.0

    # Extreme over-rotation (900 deg, 10000 deg): strictly clamped to ±1.0
    assert LogitechG27AdapterMirror.calculate_end_stop(900.0) == -1.0
    assert LogitechG27AdapterMirror.calculate_end_stop(-10000.0) == 1.0


def test_adv_g27_high_frequency_100hz_steering_oscillation():
    """Violent high-frequency steering oscillation at 100 Hz simulation frequency.
    Simulates sinusoidal steering theta(t) = 0.5 * sin(2*pi*100*t) over 500 frames (5.0s).
    Verifies that total calculated torque is always clamped within [-1.0, 1.0] without NaN or blowup.
    """
    adapter = LogitechG27AdapterMirror()
    dt = 0.001 # 1000 Hz sampling of 100 Hz oscillation (10 samples per wave cycle)
    max_observed_torque = -float('inf')
    min_observed_torque = float('inf')

    for tick in range(1000):
        t = tick * dt
        steer = 0.5 * math.sin(2.0 * math.pi * 100.0 * t)
        # Angular velocity in rad/s: d(steer)/dt * 7.854 rad
        omega = 0.5 * 2.0 * math.pi * 100.0 * math.cos(2.0 * math.pi * 100.0 * t) * 7.854

        torque = adapter.calculate_total_torque(
            self_aligning_torque_nm=1.5,
            steering_norm=steer,
            steering_angular_velocity_rad_s=omega,
            speed_mps=20.0,
            front_slip_angle_rad=0.05,
            sim_seconds=t
        )

        assert not math.isnan(torque)
        assert -1.0 <= torque <= 1.0
        max_observed_torque = max(max_observed_torque, torque)
        min_observed_torque = min(min_observed_torque, torque)

    assert min_observed_torque <= -0.99 and max_observed_torque >= 0.99, "Oscillation fully exercised bounds"


def test_adv_g27_rate_limiter_bounds():
    """FFB Slew Rate Limiter: Max delta per second is 10.0 (i.e. 0.10 per 0.01s tick).
    Verifies step target change (-1.0 -> +1.0) is rate-limited and takes exactly 20 ticks (0.20s).
    """
    adapter = LogitechG27AdapterMirror()
    dt = 0.01 # 100 Hz
    current = -1.0

    # Step target jump to +1.0
    steps = 0
    while current < 1.0 - 1e-6:
        current = adapter.apply_rate_limiter(1.0, current, dt, max_slew_rate=10.0)
        steps += 1
        assert -1.0 <= current <= 1.0

    assert steps == 20, f"Expected exactly 20 ticks to traverse 2.0 delta at 10.0/s slew rate, got {steps}"


def test_adv_g27_set_normalized_torque_bypasses_rate_limiter_defect():
    """DEFECT AUDIT: SetNormalizedTorque directly updates CurrentAppliedTorque without rate limiting.
    Calling SetNormalizedTorque(1.0) then SetNormalizedTorque(-1.0) creates an instantaneous 2.0 delta
    (infinite slew rate), bypassing the watchdog rate limiter.
    """
    adapter = LogitechG27AdapterMirror()
    adapter.set_normalized_torque(1.0)
    assert adapter.current_applied_torque == 1.0

    # Instantaneous reversal
    adapter.set_normalized_torque(-1.0)
    assert adapter.current_applied_torque == -1.0, "SetNormalizedTorque instantaneously jumped by 2.0, bypassing rate limiter"


def test_adv_g27_disconnect_stale_torque_vulnerability():
    """VULNERABILITY AUDIT: Sudden disconnection during active FFB delivery.
    In C# LogitechG27Adapter.cs: IsConnected is an auto-property { get; set; } = true.
    Setting adapter.IsConnected = false does NOT trigger Stop() or zero CurrentAppliedTorque!
    The torque remains active on hardware until another method is called.
    """
    adapter = LogitechG27AdapterMirror()
    adapter.set_normalized_torque(0.8)
    assert adapter.current_applied_torque == 0.8

    # Sudden hardware disconnection event
    adapter.is_connected = False

    # CurrentAppliedTorque remains stale at 0.8 unless Stop() is invoked!
    assert adapter.current_applied_torque == 0.8, "Vulnerability: Disconnection left stale active torque on output"
    assert not adapter.is_available, "is_available correctly returns False"


def test_adv_g27_rapid_pause_resume_cycles():
    """Rapid Pause/Resume cycles: 1,000 rapid cycles.
    Watchdog must zero torque on every pause and restore availability on resume without drift.
    """
    adapter = LogitechG27AdapterMirror()
    for _ in range(1000):
        adapter.set_normalized_torque(0.75)
        adapter.pause()
        assert adapter.current_applied_torque == 0.0
        assert not adapter.is_available
        adapter.resume()
        assert adapter.is_available
        assert adapter.current_applied_torque == 0.0


# ==============================================================================
# 3. FLOATING ORIGIN TESTS
# ==============================================================================

def test_adv_floating_origin_coordinates_at_limits():
    """Floating Origin at 10x10 km territory boundary (±5,000m) and extreme coordinates (±100,000m, ±1,000,000m).
    Origin must snap to exact multiples of 256m chunk size, keeping local coords within ±500m.
    """
    origin = FloatingOriginMirror(chunk_size_m=256, shift_threshold_m=500.0)

    for test_x in (10_000.0, -10_000.0, 100_000.0, -100_000.0, 1_000_000.0):
        origin.set_origin(0.0, 0.0, 0.0)
        shifted, delta = origin.check_and_shift(test_x, 0.0, 0.0)
        assert shifted is True
        # Origin snapped to multiple of 256
        assert origin.current_origin.x % 256 == 0
        local = origin.to_local(test_x, 0.0, 0.0)
        # Local coordinate must be centered close to zero
        assert abs(local[0]) <= 128.0 + 1e-4, f"Local X ({local[0]}) exceeded half-chunk distance at world X={test_x}"


def test_adv_floating_origin_submillimeter_precision_roundtrip():
    """Sub-millimeter roundtrip precision at 100,000 meters.
    Canonical 64-bit double world coordinate converted to local 32-bit float and back to double.
    Without floating origin (origin at 0): float precision at 100,000m is ~11.9 mm (loss of precision).
    With floating origin (origin centered): local coordinate is small, roundtrip error < 0.1 mm (sub-millimeter).
    """
    origin = FloatingOriginMirror(chunk_size_m=256, shift_threshold_m=500.0)
    world_pos = (100_000.123456, 12.5, -95_000.654321)

    # Shift origin to target area
    origin.check_and_shift(world_pos[0], world_pos[1], world_pos[2])

    local = origin.to_local(*world_pos)
    roundtrip = origin.to_world(*local)

    err_x = abs(roundtrip.x - world_pos[0])
    err_y = abs(roundtrip.y - world_pos[1])
    err_z = abs(roundtrip.z - world_pos[2])

    assert err_x < 0.0001, f"X roundtrip error ({err_x*1000:.3f} mm) exceeded 0.1 mm"
    assert err_y < 0.0001, f"Y roundtrip error ({err_y*1000:.3f} mm) exceeded 0.1 mm"
    assert err_z < 0.0001, f"Z roundtrip error ({err_z*1000:.3f} mm) exceeded 0.1 mm"


def test_adv_floating_origin_repeated_boundary_crossings_hysteresis():
    """Hysteresis verification during repeated 500m threshold crossing.
    Moving back and forth between 499m and 501m.
    Once origin shifts to 512m at 501m, moving back to 499m (distance 13m to origin) must NOT trigger a reverse shift.
    """
    origin = FloatingOriginMirror(chunk_size_m=256, shift_threshold_m=500.0)

    # Step 1: 499m (inside 500m threshold)
    s1, _ = origin.check_and_shift(499.0, 0.0, 0.0)
    assert s1 is False
    assert origin.shift_count == 0

    # Step 2: 501m (exceeds 500m threshold -> snaps to 512m)
    s2, _ = origin.check_and_shift(501.0, 0.0, 0.0)
    assert s2 is True
    assert origin.shift_count == 1
    assert origin.current_origin.x == 512.0

    # Step 3: Returns to 499m -> distance to 512m is only 13m << 500m. Must NOT shift back!
    s3, _ = origin.check_and_shift(499.0, 0.0, 0.0)
    assert s3 is False
    assert origin.shift_count == 1, "Origin thrashed back and forth without hysteresis"


def test_adv_floating_origin_velocity_invariance():
    """Velocity invariance across origin shift:
    v_canonical = (p2 - p1) / dt must equal (local_p2 + shift_delta - local_p1) / dt.
    """
    origin = FloatingOriginMirror(chunk_size_m=256, shift_threshold_m=500.0)
    p1 = 495.0
    p2 = 505.0 # crosses threshold
    dt = 0.05 # 50 ms tick

    local1 = origin.to_local(p1, 0.0, 0.0)
    shifted, delta = origin.check_and_shift(p2, 0.0, 0.0)
    assert shifted is True
    local2 = origin.to_local(p2, 0.0, 0.0)

    canonical_vel = (p2 - p1) / dt # 200 m/s
    reconstructed_vel = (local2[0] + delta[0] - local1[0]) / dt
    assert math.isclose(reconstructed_vel, canonical_vel, rel_tol=1e-5)


def test_adv_floating_origin_nan_corruption_defect():
    """DEFECT AUDIT: Passing NaN to FloatingOrigin.check_and_shift corrupts CurrentOrigin.
    In FloatingOrigin.cs: dx * dx + dz * dz is NaN. (NaN < threshold^2) is False.
    Origin is calculated as round(NaN / 256) * 256 = NaN, corrupting origin permanently.
    """
    origin = FloatingOriginMirror(chunk_size_m=256, shift_threshold_m=500.0)
    shifted, _ = origin.check_and_shift(float('nan'), 0.0, 0.0)
    assert shifted is True, "CheckAndShift proceeded on NaN input"
    assert math.isnan(origin.current_origin.x), "CurrentOrigin was corrupted with NaN"


def test_adv_floating_origin_small_threshold_infinite_shift_loop_defect():
    """DEFECT AUDIT: If shift_threshold_m < chunk_size_m * 0.5 (e.g. 100m < 128m),
    CheckAndShift triggers a shift with delta = 0 repeatedly on every frame.
    """
    origin = FloatingOriginMirror(chunk_size_m=256, shift_threshold_m=100.0)
    # At focus = 105m: distance = 105 > 100.
    # round(105 / 256) * 256 = 0 * 256 = 0.
    shifted, delta = origin.check_and_shift(105.0, 0.0, 0.0)
    assert shifted is True
    assert delta == (0.0, 0.0, 0.0), "CheckAndShift generated zero-delta shift loop"


def test_adv_floating_origin_vertical_elevation_blindness():
    """OBSERVATION / EDGE CASE: FloatingOrigin ignores Y elevation in distance calculations.
    Line 137: double distSqr = dx * dx + dz * dz; Line 147: double newOriginY = 0.0;
    If a vehicle climbs a mountain to Y = 10,000m, origin never shifts in Y.
    """
    origin = FloatingOriginMirror(chunk_size_m=256, shift_threshold_m=500.0)
    shifted, _ = origin.check_and_shift(0.0, 10_000.0, 0.0)
    assert shifted is False, "FloatingOrigin shifted on Y when distance check strictly ignores Y"
    assert origin.current_origin.y == 0.0


# ==============================================================================
# 4. RULE EVALUATOR TESTS
# ==============================================================================

def test_adv_rule_evaluator_stopline_tickrate_dependence_defect():
    """CRITICAL DEFECT AUDIT: Stop Line timer hardcodes 0.05s per tick!
    In RuleEvaluator.cs line 193:
        stopDurationAccumulated += 0.05; // Simulation tick increment or elapsed accumulation
    At 100 Hz simulation (dt = 0.01s):
        1.0 second required stop only takes 20 calls = 0.20 seconds of real simulation time!
    At 10 Hz simulation (dt = 0.10s):
        1.0 second required stop takes 20 calls = 2.0 seconds of real simulation time!
    The evaluator ignores simSeconds elapsed time delta, creating severe tick-rate dependence.
    """
    evaluator_100hz = RuleEvaluatorMirror()
    required_stop = 1.0

    # Simulate 100 Hz (dt = 0.01s)
    # In 0.20 seconds (20 ticks of 0.01s):
    for i in range(20):
        t = i * 0.01
        evaluator_100hz.evaluate_stop_line(
            sim_seconds=t, x=0.0, y=0.0, z=0.0,
            speed_mps=0.0, distance_to_stop_line=0.5,
            is_past_stop_line=False, signal_requires_stop=True,
            required_stop_seconds=required_stop
        )

    # In only 0.20s of elapsed simulation time, stopLineCompleted is already TRUE!
    assert evaluator_100hz.stop_line_completed is True, "Defect confirmed: At 100 Hz, vehicle satisfied 1.0s stop in only 0.20s real time"


def test_adv_rule_evaluator_zero_speed_crossing():
    """Stop line zero-speed crossing: vehicle creeping past stop line at 0.05 m/s without stopping.
    Speed is below stationary threshold (0.14 m/s), but vehicle did not halt in stop zone for required duration.
    Once is_past_stop_line is True, violation must trigger.
    """
    evaluator = RuleEvaluatorMirror()
    triggered, ev = evaluator.evaluate_stop_line(
        sim_seconds=5.0, x=10.0, y=0.0, z=50.0,
        speed_mps=0.05, distance_to_stop_line=-0.5,
        is_past_stop_line=True, signal_requires_stop=True,
        required_stop_seconds=1.0
    )
    assert triggered is True
    assert ev is not None
    assert ev.rule_id == RuleEvaluatorMirror.RULE_STOP_LINE
    assert ev.penalty == 25


def test_adv_rule_evaluator_reverse_corridor_boundary_jitter_cascading_defect():
    """HIGH DEFECT AUDIT: Reverse corridor boundary jitter causes cascading fatal violations.
    In RuleEvaluator.cs lines 251-277:
        if (!isInside && !boundaryViolationActive) { boundaryViolationActive = true; AddInfraction(...); }
        if (isInside) { boundaryViolationActive = false; }
    Because there is NO hysteresis and NO debounce, if a vehicle jitters across boundary X = 3.0m
    (e.g. oscillating between 3.0001 and 2.9999 at 50 Hz):
    Every single oscillation outward triggers a NEW fatal violation (100 penalty points each)!
    In 50 ticks of jitter, 25 separate fatal infractions are triggered (Total Penalty = 2500 points!).
    """
    evaluator = RuleEvaluatorMirror()
    min_x, max_x = 0.0, 3.0
    min_z, max_z = 0.0, 25.0

    violations_triggered = 0
    # 50 ticks of boundary oscillation
    for tick in range(50):
        t = tick * 0.02
        # Jitter across boundary at X = 3.0m
        x = 3.001 if (tick % 2 == 1) else 2.999
        triggered, _ = evaluator.evaluate_exercise_boundary(
            sim_seconds=t, x=x, y=0.0, z=10.0,
            min_x=min_x, max_x=max_x, min_z=min_z, max_z=max_z,
            exercise_id="reverse_corridor"
        )
        if triggered:
            violations_triggered += 1

    assert violations_triggered == 25, f"Expected 25 spam violations due to zero debounce/hysteresis, got {violations_triggered}"
    assert evaluator.total_penalty_points == 2500, f"Expected 2500 penalty points, got {evaluator.total_penalty_points}"


def test_adv_rule_evaluator_boundary_edge_exact_coordinates():
    """Boundary exact edges: inclusive comparison (x >= lowX && x <= highX).
    Coordinates exactly on the boundary line must be considered inside.
    """
    evaluator = RuleEvaluatorMirror()
    for edge_pos in ((0.0, 10.0), (3.0, 10.0), (1.5, 0.0), (1.5, 25.0)):
        triggered, _ = evaluator.evaluate_exercise_boundary(
            sim_seconds=1.0, x=edge_pos[0], y=0.0, z=edge_pos[1],
            min_x=0.0, max_x=3.0, min_z=0.0, max_z=25.0
        )
        assert triggered is False, f"Exact edge position {edge_pos} was falsely marked outside"


def test_adv_rule_evaluator_point_vs_sedan_footprint_vulnerability():
    """ARCHITECTURAL DEFECT AUDIT: Point-particle evaluation vs 3D vehicle footprint.
    Sedan dimensions: Length = 4.50m, Width = 1.80m, Mirror Width = 2.25m (half-width = 1.125m).
    Exercise boundary: Reverse Corridor X in [0.0, 3.0].
    If vehicle center is at X = 2.50m (inside [0, 3]):
        Right mirror is at X = 2.50 + 1.125 = 3.625m (0.625m OUTSIDE boundary cones!).
    RuleEvaluator evaluates only point (x, z), completely ignoring vehicle bounding box/mirrors.
    The car physically crashes into the boundary cones while the evaluator reports 0 violations.
    """
    evaluator = RuleEvaluatorMirror()
    sedan_center_x = 2.50
    mirror_half_width = 1.125
    sedan_mirror_x = sedan_center_x + mirror_half_width # 3.625m

    # Point evaluator checks vehicle center (2.50m)
    triggered, _ = evaluator.evaluate_exercise_boundary(
        sim_seconds=1.0, x=sedan_center_x, y=0.0, z=10.0,
        min_x=0.0, max_x=3.0, min_z=0.0, max_z=25.0
    )
    # Center passes without violation!
    assert triggered is False, "Center point passed inside"

    # In reality, mirror is outside
    is_mirror_outside = sedan_mirror_x > 3.0
    assert is_mirror_outside is True, "Physical vehicle mirror penetrates 62.5 cm into barrier while evaluator reports clean pass"

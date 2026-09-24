"""Pure Python reference implementation and contracts mirror for DrivingSchoolSim.
Authoritative source: Assets/DrivingSchool/Code/Contracts/Contracts.cs,
Simulation/DrivetrainMath.cs, Learning/LessonSession.cs, World/WorldRepository.cs,
Rules/SpeedLimitEvaluator.cs, Learning/TheoryPackageValidator.cs.
"""
from __future__ import annotations
import math
import os
import re
import json
import shutil
import uuid
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

class EnginePhase(Enum):
    OFF = "Off"
    IGNITION = "Ignition"
    CRANKING = "Cranking"
    RUNNING = "Running"
    STALLED = "Stalled"

class SessionPhase(Enum):
    BRIEFING = "Briefing"
    READY = "Ready"
    RUNNING = "Running"
    PASSED = "Passed"
    FAILED = "Failed"
    CANCELLED = "Cancelled"

@dataclass
class DriverCommand:
    sequence: int = 0
    steering: float = 0.0      # [-1.0 .. +1.0]
    throttle: float = 0.0      # [0.0 .. 1.0]
    brake: float = 0.0         # [0.0 .. 1.0]
    clutch: float = 0.0        # [0.0 .. 1.0] 1.0 = fully disengaged
    handbrake: bool = False
    ignition: bool = False
    starter: bool = False
    requested_gear: int = 0    # -1: R, 0: N, 1..6: Forward gears
    low_beam: bool = False
    high_beam: bool = False
    left_indicator: bool = False
    right_indicator: bool = False
    hazard_lights: bool = False
    horn: bool = False
    wipers: bool = False

    def validate(self) -> None:
        """Validates command according to C# DriverCommand.Validate(). Raises ValueError on violation."""
        def is_finite(v: float) -> bool:
            return not (math.isnan(v) or math.isinf(v))
        
        def is_unit(v: float) -> bool:
            return is_finite(v) and 0.0 <= v <= 1.0

        if (not is_finite(self.steering) or 
            not is_unit(self.throttle) or 
            not is_unit(self.brake) or 
            not is_unit(self.clutch) or 
            self.steering < -1.0 or self.steering > 1.0 or 
            self.requested_gear < -1 or self.requested_gear > 6):
            raise ValueError("DriverCommand validation failed: axis out of range or non-finite")

@dataclass
class VehicleState:
    tick: int = 0
    simulation_seconds: float = 0.0
    signed_speed_mps: float = 0.0
    engine_rpm: float = 850.0
    steering_radians: float = 0.0
    clutch_torque_nm: float = 0.0
    gear: int = 0
    engine: EnginePhase = EnginePhase.RUNNING
    left_indicator: bool = False
    right_indicator: bool = False
    low_beam: bool = False
    high_beam: bool = False
    brake_light: bool = False
    is_stalled: bool = False
    pos_x: float = 0.0
    pos_y: float = 0.0
    pos_z: float = 0.0

class DrivetrainMath:
    @staticmethod
    def axle_torque(clutch_torque_nm: float, gear_ratio: float, final_drive: float, efficiency: float) -> float:
        if final_drive <= 0.0 or efficiency < 0.0 or efficiency > 1.0:
            raise ValueError("final_drive must be > 0 and efficiency in [0, 1]")
        return clutch_torque_nm * gear_ratio * final_drive * efficiency

    @staticmethod
    def clutch_torque(engine_rad_s: float, input_shaft_rad_s: float, pedal: float, capacity_nm: float, coupling: float) -> float:
        if pedal < 0.0 or pedal > 1.0 or capacity_nm < 0.0 or coupling < 0.0:
            raise ValueError("pedal in [0, 1], capacity_nm >= 0, coupling >= 0")
        cap = capacity_nm * (1.0 - pedal)
        slip_torque = (engine_rad_s - input_shaft_rad_s) * coupling
        return max(-cap, min(cap, slip_torque))

@dataclass
class RuleEvent:
    id: str
    rule_id: str
    rule_revision: str
    participant_id: str
    evidence_id: str
    explanation_key: str
    simulation_seconds: float
    x: float
    y: float
    z: float
    penalty: int

@dataclass
class LessonDefinition:
    id: str
    title: str
    world_id: str = "training-district"
    content_status: str = "demonstration"
    time_limit_seconds: float = 120.0
    target_distance_m: float = 20.0
    stop_speed_mps: float = 0.14
    required_stop_seconds: float = 2.0
    schema_version: int = 1

@dataclass
class SessionResult:
    lesson_id: str
    phase: SessionPhase
    reason: str
    elapsed_seconds: float
    enabled_assists: List[str] = field(default_factory=list)
    events: List[RuleEvent] = field(default_factory=list)

class LessonSession:
    def __init__(self, definition: LessonDefinition):
        if (definition is None or 
            definition.time_limit_seconds <= 0.0 or 
            definition.target_distance_m <= 0.0 or 
            definition.required_stop_seconds <= 0.0 or 
            definition.stop_speed_mps < 0.0):
            raise ValueError("Invalid lesson definition parameters")
        self.definition = definition
        self.phase: SessionPhase = SessionPhase.BRIEFING
        self.result: Optional[SessionResult] = None
        self._elapsed: float = 0.0
        self._stopped: float = 0.0
        self._reached: bool = False

    def ready(self) -> None:
        if self.phase != SessionPhase.BRIEFING:
            raise RuntimeError(f"Cannot transition to Ready from {self.phase}")
        self.phase = SessionPhase.READY

    def start(self) -> None:
        if self.phase != SessionPhase.READY:
            raise RuntimeError(f"Cannot transition to Running from {self.phase}")
        self.phase = SessionPhase.RUNNING

    def tick(self, dt: float, forward_displacement_m: float, speed_mps: float) -> None:
        if (dt <= 0.0 or math.isnan(dt) or math.isinf(dt) or 
            math.isnan(speed_mps) or math.isinf(speed_mps) or 
            math.isnan(forward_displacement_m) or math.isinf(forward_displacement_m)):
            raise ValueError("Non-finite or invalid tick inputs")
        
        if self.phase != SessionPhase.RUNNING:
            return
        
        self._elapsed += dt
        if forward_displacement_m >= self.definition.target_distance_m:
            self._reached = True
        
        if self._reached and abs(speed_mps) <= self.definition.stop_speed_mps:
            self._stopped += dt
        else:
            self._stopped = 0.0
        
        if self._elapsed >= self.definition.time_limit_seconds:
            self._finish(SessionPhase.FAILED, "timeout")
        elif self._stopped >= self.definition.required_stop_seconds:
            self._finish(SessionPhase.PASSED, "stopped-after-target")

    def cancel(self) -> None:
        if self.phase in (SessionPhase.PASSED, SessionPhase.FAILED, SessionPhase.CANCELLED):
            return
        self._finish(SessionPhase.CANCELLED, "cancelled")

    def _finish(self, phase: SessionPhase, reason: str) -> None:
        self.phase = phase
        self.result = SessionResult(
            lesson_id=self.definition.id,
            phase=phase,
            reason=reason,
            elapsed_seconds=self._elapsed
        )

class WorldValidator:
    @staticmethod
    def validate(doc: Dict[str, Any]) -> None:
        if doc is None or doc.get("schemaVersion") != 1:
            raise ValueError("Unsupported world version or null doc")
        if not doc.get("id") or doc.get("chunkSizeM") != 256:
            raise ValueError("Invalid world header")
        
        for key in ("nodes", "segments", "lanes", "objects", "districts"):
            if key not in doc or not isinstance(doc[key], list):
                raise ValueError(f"Missing array: {key}")

        nodes = set()
        for n in doc["nodes"]:
            nid = n.get("id")
            if not nid or nid in nodes:
                raise ValueError(f"Duplicate/empty node: {nid}")
            nodes.add(nid)
            for coord in ("x", "y", "z"):
                val = n.get(coord)
                if val is None or math.isnan(val) or math.isinf(val):
                    raise ValueError(f"Invalid node position in {nid}")

        segments = {}
        for s in doc["segments"]:
            sid = s.get("id")
            if not sid or sid in segments:
                raise ValueError(f"Duplicate/empty segment: {sid}")
            fn = s.get("fromNode")
            tn = s.get("toNode")
            if fn not in nodes or tn not in nodes or fn == tn:
                raise ValueError(f"Broken segment: {sid}")
            w = s.get("widthM", 0)
            lc = s.get("laneCount", 0)
            sl = s.get("speedLimitKph", 0)
            if math.isnan(w) or w <= 0 or lc <= 0 or math.isnan(sl) or sl <= 0:
                raise ValueError(f"Invalid dimensions in segment: {sid}")
            segments[sid] = s

        lanes = {}
        for l in doc["lanes"]:
            lid = l.get("id")
            if not lid or lid in lanes:
                raise ValueError(f"Duplicate/empty lane: {lid}")
            sid = l.get("segmentId")
            if sid not in segments:
                raise ValueError(f"Lane {lid} references missing segment {sid}")
            s = segments[sid]
            fn = l.get("fromNode")
            tn = l.get("toNode")
            if not ((fn == s["fromNode"] and tn == s["toNode"]) or (fn == s["toNode"] and tn == s["fromNode"])):
                raise ValueError(f"Lane {lid} ends outside segment {sid}")
            w = l.get("widthM", 0)
            idx = l.get("index", -1)
            if math.isnan(w) or w <= 0 or idx < 0 or idx >= s["laneCount"]:
                raise ValueError(f"Invalid lane width/index in {lid}")
            lanes[lid] = l

        for l in doc["lanes"]:
            succs = l.get("successors", [])
            for next_id in succs:
                if next_id not in lanes:
                    raise ValueError(f"Disconnected successor {next_id} in {l['id']}")
                if l["toNode"] != lanes[next_id]["fromNode"]:
                    raise ValueError(f"Successor {next_id} fromNode != {l['id']} toNode")

        obj_ids = set()
        for o in doc["objects"]:
            oid = o.get("id")
            if not oid or oid in obj_ids:
                raise ValueError(f"Duplicate/empty object id: {oid}")
            obj_ids.add(oid)
            if not o.get("catalogId"):
                raise ValueError(f"Object {oid} has empty catalogId")
            for attr in ("x", "y", "z", "yawDeg"):
                val = o.get(attr)
                if val is None or math.isnan(val) or math.isinf(val):
                    raise ValueError(f"Invalid transform in object {oid}")

class WorldRepository:
    def __init__(self, directory: Path | str):
        self.directory = Path(directory).resolve()
        self.directory.mkdir(parents=True, exist_ok=True)

    def _resolve(self, name: str) -> Path:
        if not name or ".." in name or "/" in name or "\\" in name:
            raise ValueError("Use a plain save name")
        return self.directory / f"{name}.json"

    def save(self, name: str, doc: Dict[str, Any]) -> None:
        WorldValidator.validate(doc)
        target = self._resolve(name)
        temp = self.directory / f"{name}.json.tmp"
        backup = self.directory / f"{name}.json.bak"

        with open(temp, "w", encoding="utf-8") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)

        if target.exists():
            if backup.exists():
                backup.unlink()
            shutil.copy2(target, backup)
            temp.replace(target)
        else:
            temp.replace(target)

    def load(self, name: str) -> Dict[str, Any]:
        target = self._resolve(name)
        if not target.is_file():
            raise FileNotFoundError(f"World not found: {target}")
        with open(target, "r", encoding="utf-8") as f:
            doc = json.load(f)
        WorldValidator.validate(doc)
        return doc

class SpeedLimitEvaluator:
    RULE_ID = "pdd-10.2"
    RULE_REVISION = "2026-01"
    EXPLANATION_KEY = "rule.speed_limit_exceeded"

    def __init__(self, grace_kph: float = 20.0, penalty_points: int = 500):
        self.grace_kph = grace_kph
        self.penalty_points = penalty_points
        self.was_violating = False

    def reset(self) -> None:
        self.was_violating = False

    def evaluate(self, simulation_seconds: float, x: float, y: float, z: float, 
                 speed_mps: float, speed_limit_kph: float) -> Tuple[bool, Optional[RuleEvent]]:
        if speed_limit_kph <= 0.0 or math.isnan(speed_mps) or math.isinf(speed_mps):
            return False, None

        current_kph = abs(speed_mps) * 3.6
        is_violating = current_kph > (speed_limit_kph + self.grace_kph)

        if is_violating and not self.was_violating:
            self.was_violating = True
            event = RuleEvent(
                id=uuid.uuid4().hex,
                rule_id=self.RULE_ID,
                rule_revision=self.RULE_REVISION,
                participant_id="player",
                evidence_id=f"speed-{current_kph:.1f}-limit-{speed_limit_kph:.0f}",
                explanation_key=self.EXPLANATION_KEY,
                simulation_seconds=simulation_seconds,
                x=x, y=y, z=z,
                penalty=self.penalty_points
            )
            return True, event

        if not is_violating:
            self.was_violating = False

        return False, None

class TheoryPackageValidator:
    @staticmethod
    def validate(pack: Dict[str, Any]) -> None:
        if not pack:
            raise ValueError("Theory pack cannot be null")
        if pack.get("schemaVersion") != 1:
            raise ValueError("Unsupported schemaVersion")
        for req_str in ("id", "revision", "source"):
            if not pack.get(req_str) or not str(pack[req_str]).strip():
                raise ValueError(f"Empty theory pack {req_str}")
        
        questions = pack.get("questions")
        if not questions or not isinstance(questions, list) or len(questions) == 0:
            raise ValueError("Theory pack has no questions")

        if pack.get("isOfficial"):
            src = pack.get("source", "")
            if not any(k in src for k in ("Официальный", "ГИБДД", "Verified")):
                raise ValueError("Official status requires verified authority source")

        q_ids = set()
        for q in questions:
            qid = q.get("id")
            if not qid or qid in q_ids:
                raise ValueError(f"Duplicate or empty question id: {qid}")
            q_ids.add(qid)
            if not q.get("text") or not str(q["text"]).strip():
                raise ValueError(f"Empty question text in {qid}")
            if not q.get("explanation") or not str(q["explanation"]).strip():
                raise ValueError(f"Empty explanation in {qid}")
            answers = q.get("answers")
            if not answers or len(answers) < 2 or len(answers) > 5:
                raise ValueError(f"Question {qid} must have 2 to 5 answers")
            for ans in answers:
                if not ans or not str(ans).strip():
                    raise ValueError(f"Question {qid} has empty answer text")
            c_idx = q.get("correctIndex")
            if c_idx is None or c_idx < 0 or c_idx >= len(answers):
                raise ValueError(f"Question {qid} correctIndex out of bounds")

class ForceFeedbackWatchdog:
    def __init__(self, max_torque: float = 1.0, max_slew_rate: float = 10.0):
        self.max_torque = max_torque
        self.max_slew_rate = max_slew_rate
        self.current_torque: float = 0.0
        self.is_active: bool = True

    def set_torque(self, target: float, dt: float) -> float:
        if not self.is_active:
            self.current_torque = 0.0
            return 0.0
        if math.isnan(target) or math.isinf(target):
            self.stop()
            return 0.0
        
        clamped_target = max(-self.max_torque, min(self.max_torque, target))
        max_delta = self.max_slew_rate * max(0.0, dt)
        delta = clamped_target - self.current_torque
        if abs(delta) > max_delta:
            self.current_torque += math.copysign(max_delta, delta)
        else:
            self.current_torque = clamped_target
        return self.current_torque

    def stop(self) -> None:
        """Idempotent stop."""
        self.current_torque = 0.0
        self.is_active = False

    def resume(self) -> None:
        self.is_active = True
        self.current_torque = 0.0

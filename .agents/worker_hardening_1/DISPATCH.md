## 2026-09-19T12:07:06Z

You are the Final Hardening Worker for the Driving School Simulator Phase 1 project.
Your working directory is:
c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\worker_hardening_1

The authoritative user request is in:
c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\ORIGINAL_REQUEST.md
(and also mirrored in c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\ORIGINAL_REQUEST.md)
You MUST read this file in full before starting work.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Reference:
- Challenger 1 Report: c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\challenger_phase1_1_repl\handoff.md
- Project Blueprint: c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\orchestrator_1\PROJECT.md

Your tasks:
1. In Assets/DrivingSchool/Code/Rules/RuleEvaluator.cs:
   - Line 193: In EvaluateStopLine, replace the fixed 0.05 accumulation with dynamic simulation time dt: compute double dt = lastSimSeconds > 0 ? Math.Max(0.0, simSeconds - lastSimSeconds) : 0.01; so stop line duration is tick-rate independent.
   - In EvaluateExerciseBoundary: add spatial hysteresis or latching so microscopic perimeter jitter cannot spawn cascading duplicate fatal infractions.
2. In Assets/DrivingSchool/Code/Input/LogitechG27Adapter.cs:
   - In SetNormalizedTorque(float torque): ensure torque changes respect the slew rate limiter (SlewRateLimit = 10.0f) rather than jumping instantaneously.
   - When !IsConnected: immediately zero out CurrentAppliedTorque = 0f;.
3. In Assets/DrivingSchool/Code/World/FloatingOrigin.cs:
   - Add explicit double.IsNaN / double.IsInfinity guards in CheckAndShift and Shift to prevent origin corruption.
4. In Assets/DrivingSchool/Code/Simulation/DrivetrainMath.cs:
   - In AxleTorque and ClutchTorque: add explicit double.IsNaN / double.IsInfinity checks on inputs and throw ArgumentOutOfRangeException.
5. Run tests and verify:
   - Run Unity EditMode test runner:
     & 'E:\unityroot\6000.3.10f1\Editor\Unity.exe' -batchmode -nographics -projectPath 'c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim' -runTests -testPlatform EditMode -testResults 'c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\artifacts\reports\editmode.xml' -logFile 'c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\artifacts\reports\editmode.log'
   - Run Python test suites: python tools/run_e2e_tests.py and pytest tests/
   - Run evidence manifest verification: python tools/verify_evidence.py
6. Write handoff report to c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\worker_hardening_1\handoff.md.
Send a message back to the parent orchestrator upon completion.

## 2026-09-19T07:12:14Z
You are the Implementation and Foundation Worker for the Driving School Simulator Phase 1 project.
Your working directory is:
c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\worker_implementation_1

The authoritative user request is in:
c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\ORIGINAL_REQUEST.md
(and also mirrored in c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\ORIGINAL_REQUEST.md)
You MUST read this file in full before starting work.

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A teamwork_preview_auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Reference documents:
- Project architecture and feature inventory: c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\orchestrator_1\PROJECT.md
- Specification inventory: c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\spec_miner_survey_1\spec_inventory.md
- Architecture survey: c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\explorer_architecture_survey_1\architecture_survey.md
- Environment report: c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\explorer_codebase_survey_1\environment_report.md

Your write ownership and scope:
1. Complete C# implementations in empty assemblies to satisfy clean compilation:
   - `Assets/DrivingSchool/Code/Input/`: Implement Logitech G27 adapter (`LogitechG27Adapter.cs`) implementing `IInputSource` and `IForceFeedbackOutput`. Calculate centering spring, steering damping/friction, mechanical end-stops (900° lock-to-lock), grip loss vibration, and rate-limiting watchdog with automatic zeroing on pause/stop.
   - `Assets/DrivingSchool/Code/Rules/`: Implement traffic rule evaluation engine (`RuleEvaluator.cs`, `TrafficRules.cs`) implementing stop lines, speed limits, exercise boundaries, turn signal obedience, and penalty scoring.
2. Verify all 9 assemblies in Unity 6000.3.10f1 compile cleanly with zero errors and zero warnings.
3. In `Assets/DrivingSchool/Code/Tests/`, add unit tests covering:
   - Logitech G27 input mapping, axis normalization, calibration math, and FFB rate limiter.
   - Rule evaluator scoring, fatal penalty triggers, and exercise boundary crossing.
   - Floating origin canonical coordinate translations across 10x10 km chunks.
4. Execute Unity EditMode test runner:
   `& 'E:\unityroot\6000.3.10f1\Editor\Unity.exe' -batchmode -nographics -projectPath 'c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim' -runTests -testPlatform EditMode -testResults 'c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\artifacts\reports\editmode.xml' -logFile 'c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\artifacts\reports\editmode.log'`
   Verify all unit tests pass with exit code 0.
5. Verify and update evidence manifest (`artifacts/reports/evidence-manifest.json`) via `python tools/verify_evidence.py`.
6. Write your handoff report to:
c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\worker_implementation_1\handoff.md
Include build/test commands and output results.

Send a message back to the parent orchestrator upon completion.

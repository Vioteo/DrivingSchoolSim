## 2026-09-19T07:12:14Z
You are the E2E Test Suite Designer and Writer for the Driving School Simulator Phase 1 project.
Your working directory is:
c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\test_writer_e2e_1

The authoritative user request is in:
c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\ORIGINAL_REQUEST.md
(and also mirrored in c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\ORIGINAL_REQUEST.md)
You MUST read this file in full before starting work.

Reference documents:
- Project architecture and feature inventory: c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\orchestrator_1\PROJECT.md
- Specification inventory: c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\spec_miner_survey_1\spec_inventory.md

Your objective:
Design, implement, and run the comprehensive opaque-box E2E test suite derived from user requirements and acceptance criteria across Tiers 1-4:
1. Tier 1 - Feature Coverage: At least 5 test cases per major feature across R1-R5 (total >= 50 tests).
2. Tier 2 - Boundary & Corner Cases: At least 5 boundary/corner test cases per major feature (extreme inputs, NaN/Infinity, bounds overshoots, zero durations, reverse torque limits, missing assets).
3. Tier 3 - Cross-Feature Combinations: Pairwise interaction tests between vehicle controls, autodrome rules, UI state machine transitions, and world serialization.
4. Tier 4 - Real-World Application Scenarios: At least 5 realistic end-to-end user workflows (e.g., student selects Lesson 1, calibrates G27, starts engine, drives slalom, triggers penalty on cone touch, parks in box, finishes session, reviews debrief telemetry; theory exam workflow; custom map creation and validation workflow).

Outputs:
1. Create `TEST_INFRA.md` at project root `c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\TEST_INFRA.md` documenting test architecture, tiers, runner command, and coverage thresholds.
2. Implement executable tests in python/pytest or C# test harness as appropriate to provide reproducible test runs.
3. Run the full test suite and verify all tests pass with exit code 0.
4. Publish `TEST_READY.md` at project root `c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\TEST_READY.md` containing runner command and tier-by-tier coverage checklist.
5. Write your handoff report to:
c:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\test_writer_e2e_1\handoff.md

Send a message back to the parent orchestrator upon completion.

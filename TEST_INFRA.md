# Driving School Simulator (Phase 1) — Test Infrastructure (`TEST_INFRA.md`)

## 1. Overview and Philosophy

The Driving School Simulator (`DrivingSchoolSim`) test infrastructure is designed around **opaque-box requirement verification**, **mathematical determinism**, and **zero-engine-dependency architectural validation**.

The test suite validates the system across four production testing tiers (Tiers 1–4) derived directly from the authoritative requirements (`ORIGINAL_REQUEST.md`), architecture specifications (`PROJECT.md`), and feature inventory (`spec_inventory.md`).

Testing operates on a dual-track strategy:
1. **Automated E2E & Contract Test Suite (Python/Pytest)**: Executes fast (<0.5s), 100% reproducible end-to-end tests validating pure contracts, physical mathematics, state machines, file schemas, 3D asset geometries, UI states, and full multi-step user journeys.
2. **Unity Batchmode EditMode Runner (NUnit / C#)**: Verifies C# compilation within Unity 6000.3.10f1 URP and runs internal NUnit assembly tests (`DS.Tests.dll`).

---

## 2. Test Architecture and Directory Layout

```
DrivingSchoolSim/
├── tests/                                 # Opaque-box E2E test suite root
│   ├── __init__.py                        # Package marker
│   ├── conftest.py                        # Pytest fixtures and shared environment
│   ├── sim_contracts.py                   # High-fidelity reference contracts & simulation oracle
│   ├── test_tier1_features.py             # Tier 1: Feature Coverage (R1–R5, 60 tests)
│   ├── test_tier2_boundaries.py           # Tier 2: Boundary & Corner Cases (R1–R5, 35 tests)
│   ├── test_tier3_interactions.py         # Tier 3: Cross-Feature Combinations (15 tests)
│   └── test_tier4_scenarios.py            # Tier 4: Real-World Application Workflows (6 scenarios)
├── tools/
│   ├── run_e2e_tests.py                   # Automated test runner with reporting plugin
│   └── verify_evidence.py                 # SHA-256 evidence manifest auditor
├── Assets/DrivingSchool/Code/Tests/
│   ├── ContractTests.cs                   # NUnit Unity EditMode test suite
│   └── DS.Tests.asmdef                    # Isolated test assembly definition
├── artifacts/reports/
│   ├── e2e-test-results.json              # Execution telemetry & test results JSON
│   ├── editmode.xml                       # NUnit XML test execution output
│   └── evidence-manifest.json             # Deliverable audit manifest (73 files)
├── TEST_INFRA.md                          # This architecture document
└── TEST_READY.md                          # Readiness checklist and execution entrypoint
```

---

## 3. Tier Taxonomy & Coverage Scope

| Tier | Category | Minimum Threshold | Implemented | Scope Description |
|---|---|---|---|---|
| **Tier 1** | Feature Coverage | ≥ 5 tests / feature (≥ 50 total) | **60 tests** | Full coverage of primary happy paths across R1 (Vehicle), R2 (World & Autodrome), R3 (UI Prototype), R4 (Pure Contracts & Simulation), R5 (Tasks & Governance). |
| **Tier 2** | Boundary & Corner Cases | ≥ 5 tests / feature group | **35 tests** | Numerical extremes, NaN/Infinity inputs, axis bounds overshoots, degenerate road geometry, zero durations, reverse torque limits, missing assets, directory traversal guards. |
| **Tier 3** | Cross-Feature Interactions | Pairwise subsystem tests | **15 tests** | Inter-module couplings: DriverCommand ↔ Drivetrain torque, clutch decoupling at redline, speeding ↔ RuleEvent telemetry, editor ↔ repository atomic replace, FFB watchdog ↔ UI pause, floating origin shift ↔ canonical coordinates. |
| **Tier 4** | Real-World Application Workflows | ≥ 5 full user scenarios | **6 scenarios** | End-to-end multi-step user workflows: Lesson 1 Start/Stop, Slalom cone penalty & engine stall failure, Theory exam certification, World Editor map authoring, FFB disconnect safe recovery, Reverse box parking. |
| **Total** | **Comprehensive Suite** | **≥ 100 tests** | **116 tests** | **100% Pass Rate (Exit Code 0)** |

---

## 4. Execution Commands

### Primary Automated Runner
Runs the full suite with custom collector plugin, prints tier summary table, and exports `artifacts/reports/e2e-test-results.json`:
```powershell
python tools/run_e2e_tests.py
```

### Standard Pytest Execution
```powershell
python -m pytest tests/ -v
```

### Single Tier Execution
```powershell
# Tier 1 Feature Coverage
python -m pytest tests/test_tier1_features.py -v

# Tier 2 Boundary & Corner Cases
python -m pytest tests/test_tier2_boundaries.py -v

# Tier 3 Cross-Feature Interactions
python -m pytest tests/test_tier3_interactions.py -v

# Tier 4 Real-World Application Scenarios
python -m pytest tests/test_tier4_scenarios.py -v
```

### Unity EditMode Batchmode Runner (C01)
```powershell
$dsUnity = "E:\unityroot\6000.3.10f1\Editor\Unity.exe"
$dsProject = (Get-Location).Path
$dsArgs = @(
    '-batchmode', '-nographics',
    '-projectPath', $dsProject,
    '-runTests',
    '-testPlatform', 'EditMode',
    '-testResults', "$dsProject/artifacts/reports/editmode.xml",
    '-logFile', "$dsProject/artifacts/reports/editmode.log"
)
Start-Process -FilePath $dsUnity -ArgumentList $dsArgs -WindowStyle Hidden -PassThru -Wait
```

---

## 5. Pass/Fail Gates and Quality Thresholds

1. **Exit Code Gate**: All test commands must terminate with exit code `0`.
2. **Zero Failure Policy**: 0 test failures, 0 errors, 0 flaky tests allowed.
3. **Evidence Integrity Gate**: All 73 required project deliverables must be present on disk with valid SHA-256 hashes matching `artifacts/reports/evidence-manifest.json`.
4. **Boundary Robustness Gate**: All illegal numerical inputs (NaN, ±Inf, out-of-range gears, out-of-range pedal travel) must be rejected with explicit exceptions (`ValueError` / `ArgumentOutOfRangeException`), never causing unhandled crashes or silent corruption.
5. **Transactional Persistence Gate**: World serialization must never corrupt existing `.json` files upon failure; `.tmp` write and `.bak` backup rotation must be verified.

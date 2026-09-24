"""Driving School Simulator Phase 1 - Comprehensive E2E Test Suite Runner.
Executes Tier 1-4 tests, validates outputs, records execution telemetry,
and outputs artifacts/reports/e2e-test-results.json.
"""
import json
import os
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
import pytest

ROOT = Path(__file__).resolve().parents[1]
REPORTS_DIR = ROOT / "artifacts" / "reports"
REPORTS_DIR.mkdir(parents=True, exist_ok=True)

class E2ECollectorPlugin:
    def __init__(self):
        self.results = []
        self.start_time = 0.0

    def pytest_sessionstart(self, session):
        self.start_time = time.time()

    def pytest_runtest_logreport(self, report):
        if report.when == "call":
            node_parts = report.nodeid.split("::")
            file_name = node_parts[0]
            test_name = node_parts[-1]
            tier = "Tier 1: Feature Coverage"
            if "tier2" in file_name:
                tier = "Tier 2: Boundary & Corner Cases"
            elif "tier3" in file_name:
                tier = "Tier 3: Cross-Feature Combinations"
            elif "tier4" in file_name:
                tier = "Tier 4: Real-World Scenarios"

            self.results.append({
                "nodeid": report.nodeid,
                "file": file_name,
                "test": test_name,
                "tier": tier,
                "outcome": report.outcome.upper(),
                "duration": report.duration
            })

def main():
    print("=" * 80)
    print(" DRIVING SCHOOL SIMULATOR - E2E TEST SUITE RUNNER (TIERS 1-4)")
    print(f" Root: {ROOT}")
    print(f" Timestamp: {datetime.now(timezone.utc).isoformat()}")
    print("=" * 80)

    collector = E2ECollectorPlugin()
    exit_code = pytest.main([
        str(ROOT / "tests"),
        "-v",
        "--tb=short"
    ], plugins=[collector])

    total = len(collector.results)
    passed = sum(1 for r in collector.results if r["outcome"] == "PASSED")
    failed = sum(1 for r in collector.results if r["outcome"] == "FAILED")
    skipped = sum(1 for r in collector.results if r["outcome"] == "SKIPPED")

    tiers_summary = {}
    for r in collector.results:
        t = r["tier"]
        if t not in tiers_summary:
            tiers_summary[t] = {"total": 0, "passed": 0, "failed": 0}
        tiers_summary[t]["total"] += 1
        if r["outcome"] == "PASSED":
            tiers_summary[t]["passed"] += 1
        else:
            tiers_summary[t]["failed"] += 1

    print("\n" + "=" * 80)
    print(" TIER-BY-TIER COVERAGE SUMMARY")
    print("=" * 80)
    print(f"{'Tier Name':<38} | {'Total':<7} | {'Passed':<7} | {'Failed':<7} | {'Pass Rate':<10}")
    print("-" * 80)
    for t_name, counts in tiers_summary.items():
        rate = (counts["passed"] / counts["total"] * 100) if counts["total"] > 0 else 0
        print(f"{t_name:<38} | {counts['total']:<7} | {counts['passed']:<7} | {counts['failed']:<7} | {rate:>6.1f}%")
    print("-" * 80)
    overall_rate = (passed / total * 100) if total > 0 else 0
    print(f"{'OVERALL SUITE':<38} | {total:<7} | {passed:<7} | {failed:<7} | {overall_rate:>6.1f}%")
    print("=" * 80)

    report_payload = {
        "timestampUtc": datetime.now(timezone.utc).isoformat(),
        "runner": "python pytest 9.1.1",
        "exitCode": int(exit_code),
        "totalTests": total,
        "passed": passed,
        "failed": failed,
        "skipped": skipped,
        "passRatePercent": overall_rate,
        "tiers": tiers_summary,
        "testCases": collector.results
    }

    report_path = REPORTS_DIR / "e2e-test-results.json"
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(report_payload, f, indent=2, ensure_ascii=False)

    print(f"\n[+] Full test report saved to: {report_path}")
    if exit_code == 0:
        print("[+] STATUS: ALL TESTS PASSED SUCCESSFULLY (Exit Code 0).\n")
    else:
        print(f"[!] STATUS: TEST SUITE FAILED (Exit Code {exit_code}).\n")

    return int(exit_code)

if __name__ == "__main__":
    sys.exit(main())

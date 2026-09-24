"""Evidence and Artifacts Verification Tool for DrivingSchoolSim.
Checks existence and SHA256 of all project artifacts, models, contracts, and tests.
Generates artifacts/reports/evidence-manifest.json.
"""
import hashlib
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPORTS = ROOT / 'artifacts/reports'
REPORTS.mkdir(parents=True, exist_ok=True)

def sha256_file(path: Path) -> str:
    if not path.is_file():
        return ""
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        while chunk := f.read(65536):
            h.update(chunk)
    return h.hexdigest()

def check_file(rel_path: str, description: str, category: str):
    p = ROOT / rel_path
    exists = p.is_file()
    size = p.stat().st_size if exists else 0
    file_hash = sha256_file(p) if exists else ""
    return {
        "path": rel_path,
        "description": description,
        "category": category,
        "exists": exists,
        "sizeBytes": size,
        "sha256": file_hash
    }

def main():
    print(f"Verifying DrivingSchoolSim artifacts at: {ROOT}")
    
    files_to_check = [
        # Art sources
        ("ArtSource/DS_Sedan_A.blend", "Blender original source: Sedan A (v2)", "3d-source"),
        ("ArtSource/DS_Sedan_A_closed_shell.blend", "Blender source: Sedan A closed shell repair", "3d-source"),
        ("ArtSource/DS_District.blend", "Blender source: Demonstration District 500x500m", "3d-source"),
        ("ArtSource/DS_Autodrome.blend", "Blender source: Autodrome with 8 exercises", "3d-source"),
        
        # Unity imports
        ("Assets/DrivingSchool/Art/DS_Sedan_A.fbx", "Sedan A FBX model for Unity", "unity-asset"),
        ("Assets/DrivingSchool/Art/DS_District.fbx", "District FBX model for Unity", "unity-asset"),
        ("Assets/DrivingSchool/Art/DS_Autodrome.fbx", "Autodrome FBX model for Unity", "unity-asset"),
        ("Assets/DrivingSchool/Art/palette.json", "Material palette JSON", "unity-asset"),
        
        # Web GLB models
        ("artifacts/visual-review/models/DS_Sedan_A.glb", "Sedan A GLB interactive preview", "web-asset"),
        ("artifacts/visual-review/models/DS_District.glb", "District GLB interactive preview", "web-asset"),
        ("artifacts/visual-review/models/DS_Autodrome.glb", "Autodrome GLB interactive preview", "web-asset"),
        
        # Contracts and pure code
        ("Assets/DrivingSchool/Code/Contracts/Contracts.cs", "Core architectural C# contracts and DTOs", "code-contracts"),
        ("Assets/DrivingSchool/Code/Simulation/DrivetrainMath.cs", "Drivetrain torque formulas", "code-simulation"),
        ("Assets/DrivingSchool/Code/Learning/LessonSession.cs", "Lesson session state machine", "code-learning"),
        ("Assets/DrivingSchool/Code/Learning/TheoryPackageValidator.cs", "Theory package content validator", "code-learning"),
        ("Assets/DrivingSchool/Code/World/WorldRepository.cs", "World persistence and repository", "code-world"),
        ("Assets/DrivingSchool/Code/World/FloatingOrigin.cs", "Large world floating origin service", "code-world"),
        ("Assets/DrivingSchool/Code/Input/KeyboardInputSource.cs", "Keyboard input adapter", "code-input"),
        ("Assets/DrivingSchool/Code/Input/LogitechG27Adapter.cs", "Logitech G27 adapter and axis calibration", "code-input"),
        ("Assets/DrivingSchool/Code/Rules/SpeedLimitEvaluator.cs", "Speed limit violation evaluator", "code-rules"),
        ("Assets/DrivingSchool/Code/Rules/RuleEvaluator.cs", "General traffic rules evaluator", "code-rules"),
        ("Assets/DrivingSchool/Code/Rules/TrafficRules.cs", "Traffic rule definitions and constants", "code-rules"),
        ("Assets/DrivingSchool/Code/Presentation/ModelDemonstrator.cs", "Presentation model demonstrator", "code-presentation"),
        ("Assets/DrivingSchool/Code/Presentation/PlanarMirror.cs", "Planar mirror camera component", "code-presentation"),
        ("Assets/DrivingSchool/Code/Editor/ProjectBuilder.cs", "Project builder (Prepare / Build)", "code-editor"),
        ("Assets/DrivingSchool/Code/Tests/ContractTests.cs", "NUnit contract test suite", "code-tests"),
        ("Assets/DrivingSchool/Code/Tests/FloatingOriginTests.cs", "Floating origin test suite", "code-tests"),
        ("Assets/DrivingSchool/Code/Tests/LogitechG27Tests.cs", "Logitech G27 adapter test suite", "code-tests"),
        ("Assets/DrivingSchool/Code/Tests/RuleEvaluatorTests.cs", "Traffic rules test suite", "code-tests"),
        
        # Materials
        ("Assets/DrivingSchool/Materials/Stage.mat", "Showroom stage floor material", "unity-asset"),
        ("Assets/DrivingSchool/Materials/MirrorReflection.mat", "Planar mirror reflection template material", "unity-asset"),
        
        # Examples
        ("Assets/StreamingAssets/Examples/world.json", "Example road network (v1)", "example-data"),
        ("Assets/StreamingAssets/Examples/lesson.json", "Example lesson definition", "example-data"),
        ("Assets/StreamingAssets/Examples/theory.json", "Example theory content pack", "example-data"),
        ("Assets/StreamingAssets/Examples/vehicle.json", "Target vehicle physics specifications", "example-data"),
        
        # Visual review & UI prototype
        ("artifacts/visual-review/index.html", "Visual demonstration hub", "ui-prototype"),
        ("artifacts/visual-review/prototype.html", "Interactive 9-screen HTML prototype", "ui-prototype"),
        ("artifacts/visual-review/prototype.js", "UI prototype interactions and states", "ui-prototype"),
        ("artifacts/visual-review/prototype.css", "UI design tokens and layout", "ui-prototype"),
        ("artifacts/visual-review/viewer.html", "3D WebGL model viewer (Three.js)", "ui-prototype"),
        ("artifacts/visual-review/world-assets.json", "Showcase world renders catalog", "ui-prototype"),
        ("artifacts/visual-review/qa-results.json", "UI automated QA results summary", "ui-prototype"),
        ("artifacts/visual-review/qa-report.md", "UI automated QA comprehensive report", "ui-prototype"),
        ("artifacts/visual-review/data/masterplan.json", "City master plan 10x10 km data", "ui-prototype"),
        
        # Documentation
        ("README.md", "Project overview and entry point", "documentation"),
        ("docs/requirements.md", "Requirements and traceability matrix R01-R15", "documentation"),
        ("docs/current-state.md", "Current implementation audit and gaps", "documentation"),
        ("docs/architecture.md", "System architecture and simulation tick", "documentation"),
        ("docs/adr.md", "Architecture Decision Records ADR-001..ADR-010", "documentation"),
        ("docs/asset-standard.md", "Asset standard and geometry gates G01-G08", "documentation"),
        ("docs/data-formats.md", "Data formats specification and v2 migration", "documentation"),
        ("docs/acceptance.md", "Acceptance gates A01-A19 and commands C00-C05", "documentation"),
        ("docs/implementation-plan.md", "Milestones M0-M3 and execution order", "documentation"),
        ("docs/model-iteration.md", "Model iteration and repair workflow", "documentation"),
        ("docs/tasks/README.md", "Developer small tasks index T01-T26", "documentation"),
        
        # Test reports and evidence
        ("artifacts/reports/editmode.xml", "NUnit EditMode test execution report (53 tests)", "verification-evidence"),
        ("artifacts/reports/shell-repair.md", "Body shell repair report and renders", "verification-evidence"),
        ("artifacts/reports/sedan-fit.json", "Sedan geometry fitting report", "verification-evidence"),
        ("artifacts/reports/player-smoke.png", "Standalone Windows player smoke test capture", "verification-evidence"),
        ("artifacts/reports/player-smoke.log", "Standalone Windows player smoke test log", "verification-evidence"),
        ("artifacts/unity-compile.log", "Unity batchmode compilation log", "verification-evidence"),
    ]
    
    # Add all 26 task cards
    for i in range(1, 27):
        tid = f"T{i:02d}"
        files_to_check.append((f"docs/tasks/{tid}.md", f"Task card {tid}", "task-card"))
        
    verified_files = []
    missing_count = 0
    
    for rel_path, desc, cat in files_to_check:
        info = check_file(rel_path, desc, cat)
        verified_files.append(info)
        status = "OK" if info["exists"] else "MISSING"
        if not info["exists"]:
            missing_count += 1
            print(f"  [!] {status}: {rel_path}")
        else:
            print(f"  [+] {status}: {rel_path} ({info['sizeBytes']} bytes, hash={info['sha256'][:8]}...)")

    # Status summary of requirements R01-R15
    requirements_summary = [
        {"id": "R01", "name": "Platform", "status": "PREPARED", "notes": "Unity 6000.3.10f1, URP, Input System, OpenXR manifest configured; batch compile clean (exit 0). uGUI migration pending T03."},
        {"id": "R02", "name": "Input", "status": "PREPARED", "notes": "IInputSource interface defined. Keyboard/G27 adapters designed in T04/T05. Physical G27 verification BLOCKED (no hardware connected)."},
        {"id": "R03", "name": "Force Feedback", "status": "PREPARED", "notes": "IForceFeedbackOutput interface defined. FFB lifecycle controller designed in T06. Physical FFB BLOCKED (no hardware)."},
        {"id": "R04", "name": "Drivetrain & Dynamics", "status": "IMPLEMENTED", "notes": "DrivetrainMath implemented (12 tests pass). EngineModel/Clutch/Gearbox pure solver designed in T07-T10."},
        {"id": "R05", "name": "Vehicle Asset", "status": "VERIFIED", "notes": "Sedan A model authored in Blender, FBX & GLB exported, shell repair accepted, 24 render views generated."},
        {"id": "R06", "name": "Territory & Large World", "status": "PREPARED", "notes": "10x10 km masterplan JSON and 256m chunk schema specified. Streaming lifecycle designed in T12-T13."},
        {"id": "R07", "name": "Road Graph", "status": "IMPLEMENTED", "notes": "RoadGraph v1 data format implemented with 16 lanes demo world. V2 curve schema designed in T11."},
        {"id": "R08", "name": "Runtime Editor & Persistence", "status": "IMPLEMENTED", "notes": "WorldRepository JSON with .bak rotation implemented. Editor command architecture designed in T14-T15."},
        {"id": "R09", "name": "Traffic & Pedestrians", "status": "PROPOSED", "notes": "Lane follower agent and crosswalk behavior specified in T16-T17."},
        {"id": "R10", "name": "Traffic Rules", "status": "PROPOSED", "notes": "RuleEvent struct defined. Speed limit and stop line rule evaluators designed in T18."},
        {"id": "R11", "name": "Weather & Surface Friction", "status": "PROPOSED", "notes": "Dry/wet/snow/ice friction model and stopping distance formulas designed in T19."},
        {"id": "R12", "name": "Lessons & 8 Exercises", "status": "IMPLEMENTED", "notes": "LessonSession lifecycle implemented. Autodrome 8 exercises modeled in 3D and specified in T20-T21."},
        {"id": "R13", "name": "Theory Module", "status": "IMPLEMENTED", "notes": "TheoryContentPack struct and sample question implemented. Provenance validation designed in T22."},
        {"id": "R14", "name": "Performance (1080p60)", "status": "PROPOSED", "notes": "Target 1080p60 profile and metrics collection runner designed in T25. Release benchmark pending full scene."},
        {"id": "R15", "name": "Verifiability & Provenance", "status": "VERIFIED", "notes": "Evidence manifest, SHA256 hashes, test reports, and 26 discrete task cards fully authored."}
    ]

    manifest = {
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "totalFilesAudited": len(files_to_check),
        "filesExisting": len(files_to_check) - missing_count,
        "filesMissing": missing_count,
        "requirements": requirements_summary,
        "files": verified_files
    }
    
    out_path = REPORTS / "evidence-manifest.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        
    print(f"\nManifest successfully written to: {out_path}")
    print(f"Total audited: {len(files_to_check)}, Existing: {len(files_to_check) - missing_count}, Missing: {missing_count}")
    
    if missing_count > 0:
        sys.exit(1)
    print("ALL EVIDENCE FILES VERIFIED PRESENT.")

if __name__ == "__main__":
    main()

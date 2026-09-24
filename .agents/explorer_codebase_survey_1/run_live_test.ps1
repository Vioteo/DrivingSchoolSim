# Live verification of Unity EditMode tests

$dsProject = "C:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim"
$dsUnity = "E:\unityroot\6000.3.10f1\Editor\Unity.exe"
$reportDir = "C:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\explorer_codebase_survey_1\test_output"
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null

$xmlPath = "$reportDir\editmode_survey.xml"
$logPath = "$reportDir\editmode_survey.log"

Write-Host "Running Unity EditMode tests via: $dsUnity"
Write-Host "Project: $dsProject"

$dsArgs = @('-batchmode','-nographics','-projectPath',$dsProject,'-runTests','-testPlatform','EditMode','-testResults',$xmlPath,'-logFile',$logPath)
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $dsUnity -ArgumentList $dsArgs -WindowStyle Hidden -PassThru -Wait
$sw.Stop()

Write-Host "Process Exit Code: $($proc.ExitCode)"
Write-Host "Elapsed Time: $($sw.Elapsed.TotalSeconds) seconds"

if (Test-Path $xmlPath) {
    [xml]$res = Get-Content $xmlPath
    $tr = $res.'test-run'
    Write-Host "Test Run Result: $($tr.result), Total: $($tr.total), Passed: $($tr.passed), Failed: $($tr.failed), Skipped: $($tr.skipped)"
} else {
    Write-Host "Test results XML not generated."
}

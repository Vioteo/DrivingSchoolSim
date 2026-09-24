# Test Unity batch compilation
$proc = Start-Process -FilePath 'E:\unityroot\6000.3.10f1\Editor\Unity.exe' -ArgumentList @('-batchmode','-quit','-projectPath','C:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim','-logFile','C:\Users\AVSok\OneDrive\Documents\unity-driing\DrivingSchoolSim\.agents\explorer_codebase_survey_1\test_output\compile_test.log') -PassThru -Wait
Write-Host "ExitCode: $($proc.ExitCode)"

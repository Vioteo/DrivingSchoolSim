# Environment Probe Script for Driving School Simulator Phase 1

$ErrorActionPreference = 'Continue'

Write-Host "========================================="
Write-Host "1. PROJECT REPOSITORY & DIRECTORY CHECK"
Write-Host "========================================="

$curDir = Get-Location
Write-Host "Current Directory: $($curDir.Path)"

$gitDir = Join-Path $curDir.Path ".git"
Write-Host "Has .git in project root: $(Test-Path $gitDir)"

$parentGit = Join-Path (Split-Path $curDir.Path -Parent) ".git"
Write-Host "Has .git in parent dir: $(Test-Path $parentGit)"

Write-Host "`n========================================="
Write-Host "2. GIT CLI DETECTION"
Write-Host "========================================="

$gitCandidates = @(
    "git.exe",
    "C:\Program Files\Git\cmd\git.exe",
    "C:\Program Files\Git\bin\git.exe",
    "C:\Program Files (x86)\Git\cmd\git.exe",
    "C:\Users\AVSok\AppData\Local\Programs\Git\cmd\git.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
)

$foundGit = $null
foreach ($cand in $gitCandidates) {
    $cmd = Get-Command $cand -ErrorAction SilentlyContinue
    if ($cmd) {
        $foundGit = $cmd.Source
        Write-Host "Found Git via Get-Command: $foundGit"
        break
    } elseif (Test-Path $cand) {
        $foundGit = $cand
        Write-Host "Found Git file at: $foundGit"
        break
    }
}

if ($foundGit) {
    & $foundGit --version
    Write-Host "Checking git status with found git:"
    & $foundGit status 2>&1
} else {
    Write-Host "Git CLI NOT FOUND in standard locations or PATH."
}

Write-Host "`n========================================="
Write-Host "3. UNITY ENGINE & EDITOR DETECTION"
Write-Host "========================================="

$unityCandidates = @(
    "E:\unityroot\6000.3.10f1\Editor\Unity.exe",
    "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe",
    "C:\Program Files\Unity Hub\resources\cli\unity.exe"
)

foreach ($cand in $unityCandidates) {
    $exists = Test-Path $cand
    Write-Host "Unity path '$cand': $(if ($exists) {'EXISTS'} else {'NOT FOUND'})"
    if ($exists -and $cand -like "*Unity.exe") {
        $fvi = (Get-Item $cand).VersionInfo
        Write-Host "   FileVersion: $($fvi.FileVersion), ProductVersion: $($fvi.ProductVersion)"
    }
}

if (Test-Path "E:\unityroot") {
    Write-Host "Contents of E:\unityroot:"
    Get-ChildItem "E:\unityroot" | ForEach-Object { Write-Host "   $($_.Name)" }
}

Write-Host "`n========================================="
Write-Host "4. DOTNET SDK, RUNTIMES & MSBUILD"
Write-Host "========================================="

$dotnetCmd = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($dotnetCmd) {
    Write-Host "dotnet.exe location: $($dotnetCmd.Source)"
    Write-Host "-- dotnet --info --"
    & dotnet --info 2>&1
} else {
    Write-Host "dotnet.exe not in PATH"
}

$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    Write-Host "`nVisual Studio installations (via vswhere):"
    & $vswhere -products * -format text
}

$msbuildCandidates = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
)

foreach ($msb in $msbuildCandidates) {
    if (Test-Path $msb) {
        Write-Host "Found MSBuild: $msb"
        & $msb -version
        break
    }
}

$cscCandidates = @(
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
)

foreach ($csc in $cscCandidates) {
    if (Test-Path $csc) {
        Write-Host "Found C# Compiler (csc): $csc"
        & $csc -version
        break
    }
}

Write-Host "`n========================================="
Write-Host "5. BLENDER 3D DETECTION"
Write-Host "========================================="

$blenderPath = "C:\Program Files\Blender Foundation\Blender 5.0\blender.exe"
if (Test-Path $blenderPath) {
    Write-Host "Found Blender at: $blenderPath"
    & $blenderPath --version
} else {
    Write-Host "Blender not found at $blenderPath"
}

Write-Host "`n========================================="
Write-Host "6. PYTHON ENVIRONMENT DETECTION"
Write-Host "========================================="

$pythonCmd = Get-Command python.exe -ErrorAction SilentlyContinue
if ($pythonCmd) {
    Write-Host "python.exe in PATH: $($pythonCmd.Source)"
    & python --version
    Write-Host "Installed key pip packages (first 25):"
    & python -m pip list | Select-Object -First 25
} else {
    Write-Host "python.exe not in PATH"
}

Write-Host "`n========================================="
Write-Host "7. NODE.JS & NPM DETECTION"
Write-Host "========================================="

$nodeCmd = Get-Command node.exe -ErrorAction SilentlyContinue
if ($nodeCmd) {
    Write-Host "node.exe location: $($nodeCmd.Source)"
    $nv = & node --version
    Write-Host "Node version: $nv"
}

$npmPath = "C:\Program Files\nodejs\npm.cmd"
if (Test-Path $npmPath) {
    Write-Host "Found npm at: $npmPath"
    $npmv = & $npmPath --version
    Write-Host "npm version: $npmv"
}

Write-Host "`n========================================="
Write-Host "8. PROJECT ASSETS & TESTS QUICK SUMMARY"
Write-Host "========================================="

Write-Host "C# Source files count: $((Get-ChildItem -Path Assets -Filter *.cs -Recurse).Count)"
Write-Host "Asmdef files count: $((Get-ChildItem -Path Assets -Filter *.asmdef -Recurse).Count)"
Get-ChildItem -Path Assets -Filter *.asmdef -Recurse | Select-Object Name, DirectoryName | ForEach-Object { Write-Host "   $($_.Name) in $($_.DirectoryName)" }

Write-Host "`n========================================="
Write-Host "PROBE FINISHED"
Write-Host "========================================="

# Detailed Tooling and Compiler Survey Script

Write-Host "========================================="
Write-Host "1. MSVC C++ COMPILERS & TOOLS"
Write-Host "========================================="

$msvcDir = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC"
if (Test-Path $msvcDir) {
    Write-Host "MSVC directory exists: $msvcDir"
    $clExes = Get-ChildItem -Path $msvcDir -Filter "cl.exe" -Recurse -ErrorAction SilentlyContinue
    foreach ($cl in $clExes) {
        Write-Host "Found cl.exe: $($cl.FullName)"
    }
} else {
    Write-Host "MSVC directory not found at $msvcDir"
}

$clangCmd = Get-Command clang.exe -ErrorAction SilentlyContinue
if ($clangCmd) {
    Write-Host "Clang found: $($clangCmd.Source)"
    & clang --version
} else {
    Write-Host "Clang not in PATH"
}

$gccCmd = Get-Command gcc.exe -ErrorAction SilentlyContinue
if ($gccCmd) {
    Write-Host "GCC found: $($gccCmd.Source)"
} else {
    Write-Host "GCC not in PATH"
}

Write-Host "`n========================================="
Write-Host "2. TEST RUNNERS"
Write-Host "========================================="

$vstest = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe"
if (Test-Path $vstest) {
    Write-Host "Found vstest.console.exe: $vstest"
} else {
    Write-Host "vstest.console.exe not found at $vstest"
}

$nunitCmd = Get-Command nunit3-console.exe -ErrorAction SilentlyContinue
if ($nunitCmd) {
    Write-Host "NUnit console found: $($nunitCmd.Source)"
} else {
    Write-Host "NUnit console CLI not in PATH (standard Unity Test Framework runs via Unity batchmode)"
}

Write-Host "`n========================================="
Write-Host "3. ARCHIVE & PACKAGING TOOLS"
Write-Host "========================================="

Get-Command tar.exe -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "Found tar: $($_.Source)" }
Get-Command curl.exe -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "Found curl: $($_.Source)" }
Get-Command 7z.exe -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "Found 7z: $($_.Source)" }

Write-Host "`n========================================="
Write-Host "4. PYTHON 3D / MATH / CAD LIBRARIES"
Write-Host "========================================="

$pyPackages = & python -c "
packages = ['numpy', 'scipy', 'trimesh', 'shapely', 'matplotlib', 'pillow', 'open3d', 'pytest']
found = []
for p in packages:
    try:
        __import__(p)
        found.append(p + ': YES')
    except ImportError:
        found.append(p + ': NO')
print(', '.join(found))
"
Write-Host "Python libraries: $pyPackages"

Write-Host "`n========================================="
Write-Host "5. NODE.JS GLOBAL / LOCAL PACKAGES"
Write-Host "========================================="

if (Test-Path "package.json") {
    Write-Host "package.json found in root"
    Get-Content package.json
} else {
    Write-Host "No package.json in project root"
}

Write-Host "`n========================================="
Write-Host "6. GIT STATUS & CONFIG"
Write-Host "========================================="

$vsGit = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
if (Test-Path $vsGit) {
    Write-Host "VS Git available."
    $gitConfig = & $vsGit config --list 2>&1
    Write-Host "Git global user: $(& $vsGit config user.name) <$(& $vsGit config user.email)>"
}

Write-Host "`n========================================="
Write-Host "SURVEY SCRIPT FINISHED"
Write-Host "========================================="

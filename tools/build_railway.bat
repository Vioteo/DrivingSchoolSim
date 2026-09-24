@echo off
cd /d "%~dp0.."
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --python tools\build_railway_assets.py > railway_build.log 2>&1
echo EXIT %ERRORLEVEL% >> railway_build.log

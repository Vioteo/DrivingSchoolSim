@echo off
rem Builds the public transport kit (PT_*) from tools\transit_kit: .blend in ArtSource, .fbx in Assets\DrivingSchool\Art\Transit.
rem Close Unity before running. Pass --no-render to skip the review renders.
cd /d "%~dp0.."
if not exist artifacts\reports mkdir artifacts\reports
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup --python-exit-code 1 --python tools\build_transit.py -- %* > artifacts\reports\transit-blender.log 2>&1
echo exit code %ERRORLEVEL%
findstr /C:"ASSET_OK" /C:"TRANSIT_COMPLETE" /C:"Error" /C:"Traceback" artifacts\reports\transit-blender.log
pause

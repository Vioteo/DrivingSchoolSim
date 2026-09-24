@echo off
rem Builds all vehicles from tools\vehicle_kit: .blend in ArtSource, .fbx in Assets\DrivingSchool\Art
rem Old DS_Sedan_A.blend/.fbx are copied to ArtSource_Backup first. Close Unity before running.
cd /d "%~dp0.."
if not exist artifacts\reports mkdir artifacts\reports
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup --python-exit-code 1 --python tools\build_vehicles.py -- %* > artifacts\reports\vehicles-blender.log 2>&1
echo exit code %ERRORLEVEL%
findstr /C:"VEHICLE_OK" /C:"VEHICLES_COMPLETE" /C:"Error" /C:"Traceback" artifacts\reports\vehicles-blender.log
pause

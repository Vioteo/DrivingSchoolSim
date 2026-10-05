@echo off
rem Builds the environment kit (EN_*) from tools\env_kit: ArtSource\DS_EnvKit.blend, Assets\DrivingSchool\Art\EnvKit\*.fbx,
rem review renders in artifacts\visual-review\envkit. Pass --no-render to skip renders, --fast for quick renders.
cd /d "%~dp0.."
if not exist artifacts\reports mkdir artifacts\reports
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b --factory-startup --python-exit-code 1 --python tools\build_env_kit.py -- %* > artifacts\reports\envkit-blender.log 2>&1
echo exit code %ERRORLEVEL%
findstr /C:"ASSET_OK" /C:"ENVKIT_COMPLETE" /C:"Error" /C:"Traceback" artifacts\reports\envkit-blender.log
pause

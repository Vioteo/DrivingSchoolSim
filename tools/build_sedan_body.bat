@echo off
rem Rebuilds the sedan body: ArtSource\DS_Sedan_A.blend -> Assets\DrivingSchool\Art\DS_Sedan_A.fbx
rem Backup of the old .blend and .fbx goes to ArtSource_Backup\
cd /d "%~dp0.."
if not exist artifacts\reports mkdir artifacts\reports
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b ArtSource\DS_Sedan_A.blend --python-exit-code 1 --python tools\build_sedan_body.py > artifacts\reports\sedan-body-blender.log 2>&1
echo exit code %ERRORLEVEL%
findstr /C:"SEDAN_BODY_COMPLETE" /C:"Error" /C:"Traceback" artifacts\reports\sedan-body-blender.log
pause

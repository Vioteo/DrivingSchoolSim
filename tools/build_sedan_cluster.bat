@echo off
rem Rebuilds the sedan's instrument cluster and steering column (T72):
rem ArtSource\DS_Sedan_A.blend -> Assets\DrivingSchool\Art\DS_Sedan_A.fbx. Close Unity before running.
rem Backup of the old .blend and .fbx goes to ArtSource_Backup\
cd /d "%~dp0.."
if not exist artifacts\reports mkdir artifacts\reports
"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe" -b ArtSource\DS_Sedan_A.blend --python-exit-code 1 --python tools\build_sedan_cluster.py > artifacts\reports\sedan-cluster-blender.log 2>&1
echo exit code %ERRORLEVEL%
findstr /C:"SEDAN_CLUSTER_COMPLETE" /C:"Error" /C:"Traceback" artifacts\reports\sedan-cluster-blender.log
pause

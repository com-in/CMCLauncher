@echo off
setlocal
rem Use the script location so double-clicking works from any directory.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-and-run.ps1" %*
set "result=%errorlevel%"
if not "%result%"=="0" echo Build or launch failed. See the error above.
pause
exit /b %result%

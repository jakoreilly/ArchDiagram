@echo off
REM ArchDiagram launcher - double-click this file.
REM Runs the PowerShell launcher next to it, bypassing the execution policy
REM for this one process only (does not change machine/user policy).

setlocal
set "SCRIPT=%~dp0Launch-ArchDiagram.ps1"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*

echo.
pause
endlocal

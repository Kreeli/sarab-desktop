@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Start-SarabDesktop.ps1" -ProjectRoot "%~dp0."
set "SarabExitCode=%errorlevel%"
if not "%SarabExitCode%"=="0" pause
exit /b %SarabExitCode%

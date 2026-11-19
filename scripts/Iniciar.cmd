@echo off
setlocal
set "AUMENTOS_APP=%~dp0framework\AumentosLoL.dll"
if not exist "%AUMENTOS_APP%" set "AUMENTOS_APP=%~dp0AumentosLoL.dll"
set "AUMENTOS_HOST=%LOCALAPPDATA%\AugmentLens\dotnet\dotnet.exe"
if exist "%AUMENTOS_HOST%" goto framework
where dotnet.exe >nul 2>nul
if errorlevel 1 goto standalone
set "AUMENTOS_HOST=dotnet.exe"

:framework
"%AUMENTOS_HOST%" "%AUMENTOS_APP%" %*
exit /b %errorlevel%

:standalone
"%~dp0AumentosLoL.exe" %*
exit /b %errorlevel%

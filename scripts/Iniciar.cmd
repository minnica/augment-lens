@echo off
setlocal
set "AUMENTOS_APP=%~dp0framework\AumentosLoL.dll"
if not exist "%AUMENTOS_APP%" set "AUMENTOS_APP=%~dp0AumentosLoL.dll"
if exist "%LOCALAPPDATA%\AugmentLens\dotnet\dotnet.exe" (
    "%LOCALAPPDATA%\AugmentLens\dotnet\dotnet.exe" "%AUMENTOS_APP%" %*
    exit /b
)
where dotnet.exe >nul 2>nul
if errorlevel 1 (
    "%~dp0AumentosLoL.exe" %*
) else (
    dotnet.exe "%AUMENTOS_APP%" %*
)

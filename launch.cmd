@echo off
setlocal
cd /d "%~dp0"

set "ADMIN_EXE=%~dp0src\StevensSupportHelper.Admin\bin\Debug\net10.0-windows\StevensSupportHelper.Admin.exe"
if exist "%ADMIN_EXE%" start "" "%ADMIN_EXE%" & exit /b 0

set "ADMIN_EXE=%~dp0src\StevensSupportHelper.Admin\bin\Release\net10.0-windows\StevensSupportHelper.Admin.exe"
if exist "%ADMIN_EXE%" start "" "%ADMIN_EXE%" & exit /b 0

set "ADMIN_PROJECT=%~dp0src\StevensSupportHelper.Admin\StevensSupportHelper.Admin.csproj"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo dotnet was not found in PATH.
    pause
    exit /b 1
)

start "StevensSupportHelper Admin" cmd /k dotnet run --project "%ADMIN_PROJECT%"
endlocal

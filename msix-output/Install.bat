@echo off
cd /d "%~dp0.."

echo Stopping running instance...
taskkill /IM TaskManagementWidget.exe /F >nul 2>&1
timeout /t 2 /nobreak >nul

echo Building latest version...
dotnet build TaskManagementWidget.csproj -c Release >nul 2>&1
if %errorLevel% neq 0 (
    echo.
    echo BUILD FAILED. Run this to see errors:
    echo   dotnet build TaskManagementWidget.csproj -c Release
    pause
    exit /b 1
)

echo Deploying...
set SRC=bin\Release\net8.0-windows\win-x64
set DST=msix-output\publish-msix
copy /Y "%SRC%\TaskManagementWidget.dll"                "%DST%\TaskManagementWidget.dll"                >nul
copy /Y "%SRC%\TaskManagementWidget.pdb"                "%DST%\TaskManagementWidget.pdb"                >nul
copy /Y "%SRC%\TaskManagementWidget.deps.json"          "%DST%\TaskManagementWidget.deps.json"          >nul
copy /Y "%SRC%\TaskManagementWidget.runtimeconfig.json" "%DST%\TaskManagementWidget.runtimeconfig.json" >nul

echo Setting up auto-start on sign-in...
powershell -NoProfile -NonInteractive -Command ^
  "Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'" ^
  " -Name 'TaskManagementWidget'" ^
  " -Value '\"%~dp0publish-msix\TaskManagementWidget.exe\"'" ^
  " -Type String -Force"

echo.
echo Launching...
start "" "%~dp0publish-msix\TaskManagementWidget.exe"

echo.
echo Done! Task Management Widget is running and will auto-start on sign-in.

@echo off
setlocal

:: Self-elevate to admin
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Requesting administrator privileges...
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo Stopping TASKly...
taskkill /IM TaskManagementWidget.exe /F >nul 2>&1

echo Uninstalling TASKly...
powershell -NoProfile -NonInteractive -Command "Get-AppxPackage -Name 'TASKly' | Remove-AppxPackage"

echo Removing certificate...
powershell -NoProfile -NonInteractive -Command "Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -eq 'CN=TaskWidget' } | Remove-Item"

echo Removing auto-start entry...
powershell -NoProfile -NonInteractive -Command "Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'TaskManagementWidget' -ErrorAction SilentlyContinue"

echo.
echo TASKly uninstalled.
pause

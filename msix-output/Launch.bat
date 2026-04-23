@echo off
cd /d "%~dp0.."
taskkill /IM TaskManagementWidget.exe /F >nul 2>&1
timeout /t 2 /nobreak >nul
dotnet publish TaskManagementWidget.csproj -c Release -o msix-output\publish-msix -p:PublishSingleFile=false -p:SelfContained=true -p:RuntimeIdentifier=win-x64 >nul 2>&1
copy /Y bin\Release\net8.0-windows\win-x64\TaskManagementWidget.dll msix-output\publish-msix\TaskManagementWidget.dll >nul 2>&1
start "" "%~dp0publish-msix\TaskManagementWidget.exe"


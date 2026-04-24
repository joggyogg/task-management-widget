@echo off
setlocal
cd /d "%~dp0"

:: Self-elevate to admin
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Requesting administrator privileges...
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo Uninstalling previous version (if any)...
powershell -NoProfile -NonInteractive -Command "Get-AppxPackage -Name 'TASKly' | Remove-AppxPackage" >nul 2>&1

echo Installing TASKly certificate...
powershell -NoProfile -NonInteractive -Command "Import-Certificate -FilePath '%~dp0TASKly.cer' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null"
if %errorLevel% neq 0 (
    echo ERROR: Failed to install certificate.
    pause
    exit /b 1
)

echo Installing TASKly...
powershell -NoProfile -NonInteractive -Command "Add-AppxPackage -Path '%~dp0TASKly.msix'"
if %errorLevel% neq 0 (
    echo ERROR: Installation failed. If the error mentions 'choose where to get apps',
    echo go to Settings ^> Apps ^> Advanced app settings and set it to Anywhere.
    pause
    exit /b 1
)

echo.
echo TASKly installed successfully!
echo Launching TASKly...
powershell -NoProfile -NonInteractive -Command "& { $pkg = Get-AppxPackage -Name 'TASKly'; Start-Process ('shell:AppsFolder\' + $pkg.PackageFamilyName + '!App') }"
pause

@echo off
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo Stopping running instance...
powershell -NoProfile -NonInteractive -Command "Get-Process -Name 'TaskManagementWidget' -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 800"

echo Installing certificate...
powershell -NoProfile -NonInteractive -Command "$c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2('%~dp0TaskManagementWidget.cer'); foreach ($s in @('TrustedPeople','Root')) { $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($s,'LocalMachine'); $store.Open('ReadWrite'); $store.Add($c); $store.Close(); Write-Host ('Added to ' + $s) }"

echo.
echo Installing app...
powershell -NoProfile -NonInteractive -Command "Add-AppxPackage -Path '%~dp0TaskManagementWidget.msix' -ForceUpdateFromAnyVersion"

echo.
echo Enabling auto-start on sign-in...
powershell -NoProfile -NonInteractive -Command "$pkg = Get-AppxPackage -Name 'TaskManagementWidget' | Sort-Object Version -Descending | Select-Object -First 1; if (-not $pkg) { throw 'Package not found after install.' }; $aumid = $pkg.PackageFamilyName + '!App'; $runValue = 'explorer.exe shell:AppsFolder\' + $aumid; New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Force | Out-Null; Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'TaskManagementWidget' -Value $runValue; Write-Host 'Auto-start registered.'"

echo.
echo Launching app...
powershell -NoProfile -NonInteractive -Command "$pkg = Get-AppxPackage -Name 'TaskManagementWidget' | Sort-Object Version -Descending | Select-Object -First 1; if ($pkg) { Start-Process ('shell:AppsFolder\' + $pkg.PackageFamilyName + '!App') }"

echo.
echo Done!
pause

@echo off
:: Self-elevate if not already admin
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo Installing certificate...
powershell -NoProfile -NonInteractive -Command ^
  "$c = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2('%~dp0TaskManagementWidget.cer');" ^
  "foreach ($s in @('TrustedPeople','Root')) {" ^
  "  $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($s,'LocalMachine');" ^
  "  $store.Open('ReadWrite'); $store.Add($c); $store.Close();" ^
  "  Write-Host \"Added to $s\"" ^
  "}"

echo.
echo Installing app...
powershell -NoProfile -NonInteractive -Command ^
  "Add-AppxPackage -Path '%~dp0TaskManagementWidget.msix' -ForceUpdateFromAnyVersion"

echo.
echo Enabling auto-start on sign-in...
powershell -NoProfile -NonInteractive -Command ^
  "$pkg = Get-AppxPackage -Name 'TaskManagementWidget' | Sort-Object Version -Descending | Select-Object -First 1;" ^
  "if (-not $pkg) { throw 'TaskManagementWidget package not found after install.' }" ^
  "$aumid = $pkg.PackageFamilyName + '!App';" ^
  "$runValue = 'explorer.exe shell:AppsFolder\' + $aumid;" ^
  "New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Force | Out-Null;" ^
  "Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'TaskManagementWidget' -Value $runValue -Type String;" ^
  "Write-Host ('Auto-start entry created: ' + $runValue)"

echo.
echo Launching app now...
powershell -NoProfile -NonInteractive -Command ^
  "$pkg = Get-AppxPackage -Name 'TaskManagementWidget' | Sort-Object Version -Descending | Select-Object -First 1;" ^
  "if ($pkg) {" ^
  "  $aumid = $pkg.PackageFamilyName + '!App';" ^
  "  Start-Process explorer.exe ('shell:AppsFolder\' + $aumid);" ^
  "  Write-Host 'Task Management Widget launched.'" ^
  "} else { Write-Host 'Could not launch app: package not found.' }"

echo.
echo Done! "Task Management Widget" is launched now and will also start automatically when you sign in to Windows.
pause

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
echo Done! Find "Task Management Widget" in the Start Menu.
pause

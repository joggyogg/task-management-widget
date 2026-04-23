@echo off
setlocal

:: Self-elevate if not already admin (needed for LocalMachine cert cleanup)
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo Removing auto-start entry...
powershell -NoProfile -NonInteractive -Command ^
  "Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'TaskManagementWidget' -ErrorAction SilentlyContinue"

echo.
echo Uninstalling app package...
powershell -NoProfile -NonInteractive -Command ^
  "$pkgs = Get-AppxPackage -Name 'TaskManagementWidget' -ErrorAction SilentlyContinue;" ^
  "if ($pkgs) { $pkgs | ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop }; Write-Host 'Package removed.' } else { Write-Host 'Package not found for current user.' }"

echo.
echo Removing certificate from LocalMachine stores...
powershell -NoProfile -NonInteractive -Command ^
  "$thumb = $null;" ^
  "if (Test-Path '%~dp0TaskManagementWidget.cer') {" ^
  "  try { $thumb = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2('%~dp0TaskManagementWidget.cer')).Thumbprint } catch {}" ^
  "}" ^
  "foreach ($storeName in @('TrustedPeople','Root')) {" ^
  "  try {" ^
  "    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'LocalMachine');" ^
  "    $store.Open('ReadWrite');" ^
  "    if ($thumb) {" ^
  "      $matches = $store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $thumb, $false);" ^
  "      foreach ($m in $matches) { $store.Remove($m) }" ^
  "    } else {" ^
  "      $matches = $store.Certificates | Where-Object { $_.Subject -eq 'CN=TaskWidget' };" ^
  "      foreach ($m in $matches) { $store.Remove($m) }" ^
  "    }" ^
  "    $store.Close();" ^
  "    Write-Host ('Checked store: ' + $storeName);" ^
  "  } catch { Write-Host ('Could not modify store: ' + $storeName) }" ^
  "}"

echo.
set "TASKDATA=%APPDATA%\TaskManagementWidget"
set "BACKUPDIR=%LOCALAPPDATA%\TaskManagementWidgetBackup"
set "BACKUPFILE=%BACKUPDIR%\tasks.json"
echo AppData task folder (unpackaged): %TASKDATA%
echo Backup location (survives uninstall): %BACKUPFILE%
choice /C YN /N /M "Also delete saved task data? (Y=Delete permanently, N=Keep for reinstall): "
if errorlevel 2 goto keepdata
if errorlevel 1 goto deletedata

:deletedata
if exist "%TASKDATA%" (
    rmdir /S /Q "%TASKDATA%"
    echo Saved task data deleted.
) else (
    echo No task data folder found.
)
if exist "%BACKUPFILE%" (
    del /Q "%BACKUPFILE%"
    echo Removed previous backup file.
)
goto done

:keepdata
echo Backing up saved task data for reinstall...
powershell -NoProfile -NonInteractive -Command ^
  "$pkg = Get-AppxPackage -Name 'TaskManagementWidget' | Sort-Object Version -Descending | Select-Object -First 1;" ^
  "$src1 = Join-Path $env:APPDATA 'TaskManagementWidget\tasks.json';" ^
  "$src2 = $null;" ^
  "if ($pkg) { $src2 = Join-Path $env:LOCALAPPDATA ('Packages\\' + $pkg.PackageFamilyName + '\\LocalCache\\Roaming\\TaskManagementWidget\\tasks.json') }" ^
  "$src = $null;" ^
  "if (Test-Path $src1) { $src = $src1 } elseif ($src2 -and (Test-Path $src2)) { $src = $src2 }" ^
  "if ($src) {" ^
  "  New-Item -ItemType Directory -Path '%BACKUPDIR%' -Force | Out-Null;" ^
  "  Copy-Item $src '%BACKUPFILE%' -Force;" ^
  "  Write-Host ('Backup saved: ' + '%BACKUPFILE%');" ^
  "} else { Write-Host 'No task file found to back up.' }"
echo Saved task data kept (backup attempted).

:done
echo.
echo Uninstalling app package...
powershell -NoProfile -NonInteractive -Command ^
  "$pkgs = Get-AppxPackage -Name 'TaskManagementWidget' -ErrorAction SilentlyContinue;" ^
  "if ($pkgs) { $pkgs | ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop }; Write-Host 'Package removed.' } else { Write-Host 'Package not found for current user.' }"

echo.
echo Removing certificate from LocalMachine stores...
powershell -NoProfile -NonInteractive -Command ^
  "$thumb = $null;" ^
  "if (Test-Path '%~dp0TaskManagementWidget.cer') {" ^
  "  try { $thumb = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2('%~dp0TaskManagementWidget.cer')).Thumbprint } catch {}" ^
  "}" ^
  "foreach ($storeName in @('TrustedPeople','Root')) {" ^
  "  try {" ^
  "    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName, 'LocalMachine');" ^
  "    $store.Open('ReadWrite');" ^
  "    if ($thumb) {" ^
  "      $matches = $store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $thumb, $false);" ^
  "      foreach ($m in $matches) { $store.Remove($m) }" ^
  "    } else {" ^
  "      $matches = $store.Certificates | Where-Object { $_.Subject -eq 'CN=TaskWidget' };" ^
  "      foreach ($m in $matches) { $store.Remove($m) }" ^
  "    }" ^
  "    $store.Close();" ^
  "    Write-Host ('Checked store: ' + $storeName);" ^
  "  } catch { Write-Host ('Could not modify store: ' + $storeName) }" ^
  "}"

echo Done. Task Management Widget has been uninstalled and startup entry removed.
pause

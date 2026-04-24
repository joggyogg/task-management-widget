$ErrorActionPreference = "Stop"
$projectDir  = "c:\Users\joggy\Documents\Task Management Widget\task-management-widget"
$publishDir  = "$projectDir\publish"
$msixDir     = "$projectDir\msix-output"
$pkgLayout   = "$msixDir\PackageLayout"
$outputMsix  = "$msixDir\TASKly.msix"
$cerPath     = "$msixDir\TASKly.cer"
$pfxPath     = "$msixDir\sign.pfx"
$pfxPassword = "WidgetSign2026!"

Set-Location $projectDir

# ── 0. Publish (no single-file — MSIX sandbox blocks temp extraction) ─────────
Write-Host "Cleaning previous build output..." -ForegroundColor Yellow
dotnet clean TaskManagementWidget.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet clean failed" }

Write-Host "Publishing app for MSIX..." -ForegroundColor Yellow
dotnet publish TaskManagementWidget.csproj -c Release -o "$msixDir\publish-msix" `
    -p:PublishSingleFile=false -p:SelfContained=true -p:RuntimeIdentifier=win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Write-Host "Publish complete" -ForegroundColor Green

# ── 1. Self-signed code-signing certificate ──────────────────────────────────
$certSubject = "CN=TaskWidget"
$cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $certSubject } |
        Select-Object -First 1

if (-not $cert) {
    Write-Host "Creating self-signed certificate..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $certSubject `
        -KeyUsage DigitalSignature `
        -FriendlyName "Task Management Widget" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -NotAfter (Get-Date).AddYears(5)
    Write-Host "Certificate created: $($cert.Subject)" -ForegroundColor Green
} else {
    Write-Host "Using existing certificate: $($cert.Subject)" -ForegroundColor Green
}

$publisher = $cert.Subject   # must exactly match manifest Publisher

# ── 2. Export .cer and .pfx ───────────────────────────────────────────────────
New-Item -ItemType Directory $msixDir -Force | Out-Null
Remove-Item "$msixDir\TaskManagementWidget.cer" -Force -ErrorAction SilentlyContinue
Remove-Item "$msixDir\TaskManagementWidget.msix" -Force -ErrorAction SilentlyContinue
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
$sec = ConvertTo-SecureString $pfxPassword -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $sec | Out-Null
Write-Host "Exported cert files to msix-output\" -ForegroundColor Green

# ── 3. Package layout ─────────────────────────────────────────────────────────
Remove-Item $pkgLayout -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory "$pkgLayout\Assets" -Force | Out-Null
Copy-Item "$msixDir\publish-msix\*" $pkgLayout -Recurse -Force
Write-Host "Copied publish files to layout" -ForegroundColor Green

# ── 4. Logo PNGs via System.Drawing ──────────────────────────────────────────
Add-Type -AssemblyName System.Drawing

function New-LogoPng([string]$path, [int]$width, [int]$height) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode        = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint    = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    # Dark background
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(30, 30, 46))
    $g.FillRectangle($bg, 0, 0, $width, $height)

    # Heart emoji centred
    $sz       = [Math]::Min($width, $height)
    $fontSize = [float]($sz * 0.58)
    $font     = New-Object System.Drawing.Font("Segoe UI Emoji", $fontSize, [System.Drawing.FontStyle]::Regular)
    $fg       = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(243, 139, 168))
    $sf       = New-Object System.Drawing.StringFormat
    $sf.Alignment     = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF(0, 0, $width, $height)
    $g.DrawString("`u{2764}", $font, $fg, $rect, $sf)

    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

New-LogoPng "$pkgLayout\Assets\Square44x44Logo.png"   44  44
New-LogoPng "$pkgLayout\Assets\Square150x150Logo.png" 150 150
New-LogoPng "$pkgLayout\Assets\StoreLogo.png"          50  50
New-LogoPng "$pkgLayout\Assets\Wide310x150Logo.png"   310 150
Write-Host "Created logo assets" -ForegroundColor Green

# ── 5. AppxManifest.xml ───────────────────────────────────────────────────────
$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
         xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
         xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap uap5 rescap">
  <Identity Name="TASKly"
            Publisher="$publisher"
            Version="1.0.0.0"
            ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>TASKly</DisplayName>
    <PublisherDisplayName>TASKly</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" />
  </Dependencies>
  <Resources>
    <Resource Language="en-us"/>
  </Resources>
  <Applications>
    <Application Id="App" Executable="TaskManagementWidget.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="TASKly"
        Description="TASKly - desktop task management widget"
        BackgroundColor="#1e1e2e"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png"
        AppListEntry="default">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
      </uap:VisualElements>
      <Extensions>
        <uap5:Extension Category="windows.startupTask">
          <uap5:StartupTask TaskId="TASKlyStartup" Enabled="true" DisplayName="TASKly"/>
        </uap5:Extension>
      </Extensions>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@
Set-Content "$pkgLayout\AppxManifest.xml" -Value $manifest -Encoding UTF8
Write-Host "Created AppxManifest.xml (Publisher: $publisher)" -ForegroundColor Green

# ── 6. Get makeappx.exe from NuGet cache (download if missing) ────────────────
$nugetCache = "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools"

function Find-MakeAppx {
    Get-ChildItem $nugetCache -Recurse -Filter "makeappx.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*x64*" } |
        Select-Object -First 1
}

$makeappxItem = Find-MakeAppx
if (-not $makeappxItem) {
    Write-Host "Downloading Windows SDK build tools via NuGet (one-time ~200 MB)..." -ForegroundColor Yellow
    $tempProj = "$env:TEMP\sdktools_$(Get-Random)"
    New-Item -ItemType Directory $tempProj -Force | Out-Null
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.22621.756" />
  </ItemGroup>
</Project>
"@ | Set-Content "$tempProj\tools.csproj"
    dotnet restore "$tempProj\tools.csproj"
    $makeappxItem = Find-MakeAppx
}

if (-not $makeappxItem) { throw "Could not locate makeappx.exe after NuGet restore." }

$makeappx = $makeappxItem.FullName
$signtool  = Join-Path $makeappxItem.DirectoryName "signtool.exe"
Write-Host "makeappx: $makeappx" -ForegroundColor Green

# ── 7. Pack ───────────────────────────────────────────────────────────────────
Remove-Item $outputMsix -Force -ErrorAction SilentlyContinue
Write-Host "Packing MSIX..." -ForegroundColor Yellow
& $makeappx pack /d $pkgLayout /p $outputMsix /nv /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed (exit $LASTEXITCODE)" }

# ── 8. Sign ───────────────────────────────────────────────────────────────────
Write-Host "Signing MSIX..." -ForegroundColor Yellow
& $signtool sign /fd sha256 /f $pfxPath /p $pfxPassword $outputMsix
if ($LASTEXITCODE -ne 0) { throw "signtool sign failed (exit $LASTEXITCODE)" }

# ── Done ──────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "==========================================" -ForegroundColor Green
Write-Host "  TASKly package ready!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Send your wife BOTH of these files:" -ForegroundColor Yellow
Write-Host "  1. $cerPath" -ForegroundColor Cyan
Write-Host "  2. $outputMsix" -ForegroundColor Cyan
Write-Host ""
Write-Host "To install: right-click Install.bat -> Run as administrator" -ForegroundColor Yellow
Write-Host ""

# ── 9. Generate Install.bat ───────────────────────────────────────────────────
$installBat = @'
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
'@
Set-Content "$msixDir\Install.bat" -Value $installBat -Encoding ASCII

# ── 10. Generate Uninstall.bat ────────────────────────────────────────────────
$uninstallBat = @'
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
'@
Set-Content "$msixDir\Uninstall.bat" -Value $uninstallBat -Encoding ASCII
Write-Host "Generated Install.bat and Uninstall.bat" -ForegroundColor Green

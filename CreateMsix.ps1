$ErrorActionPreference = "Stop"
$projectDir  = "c:\Users\joggy\Documents\Task Management Widget\task-management-widget"
$publishDir  = "$projectDir\publish"
$msixDir     = "$projectDir\msix-output"
$pkgLayout   = "$msixDir\PackageLayout"
$outputMsix  = "$msixDir\TaskManagementWidget.msix"
$cerPath     = "$msixDir\TaskManagementWidget.cer"
$pfxPath     = "$msixDir\sign.pfx"
$pfxPassword = "WidgetSign2026!"

Set-Location $projectDir

# ── 0. Publish (no single-file — MSIX sandbox blocks temp extraction) ─────────
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
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # Dark background
    $bg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(30, 30, 46))
    $g.FillRectangle($bg, 0, 0, $width, $height)

    # Centred circle (based on shortest side)
    $sz     = [Math]::Min($width, $height)
    $margin = [int]($sz * 0.12)
    $cSz    = $sz - $margin * 2
    $cx     = [int](($width  - $cSz) / 2)
    $cy     = [int](($height - $cSz) / 2)
    $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(137, 180, 250))
    $g.FillEllipse($accent, $cx, $cy, $cSz, $cSz)

    # Letter "T"
    $fontSize = [float]($sz * 0.42)
    $font     = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold)
    $fg       = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(30, 30, 46))
    $sf       = New-Object System.Drawing.StringFormat
    $sf.Alignment     = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF(0, 0, $width, $height)
    $g.DrawString("T", $font, $fg, $rect, $sf)

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
         xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
         IgnorableNamespaces="uap rescap">
  <Identity Name="TaskManagementWidget"
            Publisher="$publisher"
            Version="1.0.0.0"
            ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>Task Management Widget</DisplayName>
    <PublisherDisplayName>TaskWidget</PublisherDisplayName>
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
        DisplayName="Task Management Widget"
        Description="A desktop task management widget"
        BackgroundColor="#1e1e2e"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
      </uap:VisualElements>
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
Write-Host "  MSIX package ready!" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Send your wife BOTH of these files:" -ForegroundColor Yellow
Write-Host "  1. $cerPath" -ForegroundColor Cyan
Write-Host "  2. $outputMsix" -ForegroundColor Cyan
Write-Host ""
Write-Host "INSTALL STEPS (she does this once):" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Step 1 - Install the certificate:" -ForegroundColor White
Write-Host "    Double-click  TaskManagementWidget.cer"
Write-Host "    -> Open  ->  Install Certificate"
Write-Host "    -> Local Machine  ->  Next"
Write-Host "    -> 'Place all certificates in the following store'  ->  Browse"
Write-Host "    -> Select 'Trusted People'  ->  OK  ->  Next  ->  Finish"
Write-Host ""
Write-Host "  Step 2 - Install the app:" -ForegroundColor White
Write-Host "    Double-click  TaskManagementWidget.msix  ->  Install"
Write-Host ""
Write-Host "  If the Install button is greyed out:" -ForegroundColor DarkYellow
Write-Host "    Settings -> Apps -> Advanced app settings"
Write-Host "    -> 'Choose where to get apps'  -> set to Anywhere"
Write-Host ""

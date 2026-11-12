#requires -Version 5
<#
  Builds everything: tests, the self-contained companion, and the Game Bar widget package.
  Usage: .\build.ps1 [-Configuration Debug|Release] [-Register] [-Package]
    -Register  also registers the built (loose-layout) package for the current user. Requires Windows Developer Mode.
    -Package   (Release) also produces artifacts\package: a self-signed .msix, its .cer, dependencies and Install.ps1
               for installing on another PC (e.g. a handheld) without Developer Mode.
#>
param(
  [ValidateSet('Debug', 'Release')] [string]$Configuration = 'Debug',
  [switch]$Register,
  [switch]$Package
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

function Step($name, [scriptblock]$body) {
  Write-Host "==> $name" -ForegroundColor Cyan
  & $body | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "$name failed (exit $LASTEXITCODE)" }
}

Step 'Unit tests' { dotnet test "$root\tests\SaveSync.Core.Tests" -c $Configuration --nologo -v q }

$companionOut = "$root\artifacts\companion"
if (Test-Path $companionOut) { Remove-Item $companionOut -Recurse -Force }
Step 'Publish companion' {
  dotnet publish "$root\src\SaveSync.Companion" -c $Configuration -r win-x64 --self-contained true -o $companionOut --nologo -v q
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild from Visual Studio (with the UWP workload) was not found.' }

Step 'Build widget package' {
  & $msbuild "$root\src\SaveSync.Widget\SaveSync.Widget.csproj" /restore /m /nologo /v:m `
    /p:Configuration=$Configuration /p:Platform=x64 /p:AppxPackageSigningEnabled=false
}

$layout = "$root\artifacts\layout\$Configuration"
$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe (Windows SDK) was not found.' }
$pkgDirPattern = if ($Configuration -eq 'Debug') { '*_x64_Debug_Test' } else { '*_x64_Test' }
$msix = Get-ChildItem "$root\src\SaveSync.Widget\AppPackages\$pkgDirPattern\*.msix" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msix) { throw 'The widget .msix was not produced.' }
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
Step 'Unpack package layout' { & $makeappx.FullName unpack /p $msix.FullName /d $layout /nv /o }
Write-Host "Package layout: $layout" -ForegroundColor Green

if ($Package) {
  if ($Configuration -ne 'Release') { throw '-Package requires -Configuration Release (Debug builds need a framework that is not shipped).' }
  $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1
  if (-not $signtool) { throw 'signtool.exe (Windows SDK) was not found.' }

  # The certificate subject must equal the Publisher in Package.appxmanifest.
  $publisher = ([xml](Get-Content "$layout\AppxManifest.xml")).Package.Identity.Publisher
  $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
  if (-not $cert) {
    Write-Host "Creating self-signed certificate $publisher..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature -FriendlyName 'Save Sync Tracker' `
      -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(3) `
      -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
  }

  $pkgOut = "$root\artifacts\package"
  if (Test-Path $pkgOut) { Remove-Item $pkgOut -Recurse -Force }
  New-Item $pkgOut -ItemType Directory | Out-Null
  $signed = "$pkgOut\SaveSyncTracker.msix"
  Copy-Item $msix.FullName $signed
  Step 'Sign package' { & $signtool.FullName sign /fd SHA256 /sha1 $cert.Thumbprint $signed }
  Export-Certificate -Cert $cert -FilePath "$pkgOut\SaveSyncTracker.cer" | Out-Null
  Copy-Item "$($msix.DirectoryName)\Dependencies\x64" "$pkgOut\Dependencies" -Recurse
  Copy-Item "$root\tools\Install.ps1" $pkgOut
  Write-Host "Installable package: $pkgOut (copy the folder to the target PC and run Install.ps1 as admin)" -ForegroundColor Green
}

if ($Register) {
  Step 'Register package' { Add-AppxPackage -Register "$layout\AppxManifest.xml" -ForceApplicationShutdown; $global:LASTEXITCODE = 0 }
  Write-Host 'Registered. Press Win+G and open "Save Sync Tracker" from the widget menu.' -ForegroundColor Green
}

#requires -Version 5
<#
  Installs the Save Sync Tracker Game Bar widget from this folder. Run from an elevated PowerShell:
    powershell -ExecutionPolicy Bypass -File .\Install.ps1
  Trusts the bundled self-signed certificate (machine-wide, TrustedPeople), then installs the signed package
  together with its framework dependencies. Developer Mode is not required.
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot

$admin = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $admin.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw 'Run this script from an elevated (Administrator) PowerShell.'
}

$cer = Get-ChildItem $here -Filter *.cer | Select-Object -First 1
$msix = Get-ChildItem $here -Filter *.msix | Select-Object -First 1
if (-not $cer -or -not $msix) { throw 'Expected a .cer and a .msix next to this script.' }

Write-Host "Trusting certificate $($cer.Name)..."
Import-Certificate -FilePath $cer.FullName -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

$deps = @(Get-ChildItem "$here\Dependencies" -Filter *.appx -ErrorAction SilentlyContinue | ForEach-Object FullName)
Write-Host "Installing $($msix.Name)..."
Add-AppxPackage -Path $msix.FullName -DependencyPath $deps -ForceApplicationShutdown

Write-Host 'Installed. Press Win+G and open "Save Sync Tracker" from the widget menu.' -ForegroundColor Green

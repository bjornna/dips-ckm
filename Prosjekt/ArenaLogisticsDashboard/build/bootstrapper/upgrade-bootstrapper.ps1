param([string]$bootstrapperDir)

$sourcePath = Join-Path $PSScriptRoot 'bootstrapper.ps1'
$destinationPath = Join-Path $bootstrapperDir 'bootstrapper.ps1'

Write-Host "Copying $sourcePath to $destinationPath"
Copy-Item -Path $sourcePath -Destination $destinationPath -Force

$sourcePath = Join-Path $PSScriptRoot 'bootstrapper-module.psm1'
$destinationPath = Join-Path $bootstrapperDir 'bootstrapper-module.psm1'

Write-Host "Copying $sourcePath to $destinationPath"
Copy-Item -Path $sourcePath -Destination $destinationPath -Force

$bootstrapperConfigPath = Join-Path $bootstrapperDir 'bootstrapper.config'
if (-not (Test-Path $bootstrapperConfigPath))
{
    $sourcePath = Join-Path $PSScriptRoot 'bootstrapper.config'
    $destinationPath = Join-Path $bootstrapperDir 'bootstrapper.config'

    Write-Host "Copying $sourcePath to $destinationPath"
    Copy-Item -Path $sourcePath -Destination $destinationPath -Force
}
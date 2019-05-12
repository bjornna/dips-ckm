[cmdletbinding()]
param([string]$runType = 'full')

$ErrorActionPreference = "Stop"

$runTypeLower = $runType.ToLower();

$bootstrapperModule = (Join-Path $PSScriptRoot 'bootstrapper-module.psm1')
Import-Module -Name $bootstrapperModule

function Get-DomainName([string]$runType)
{
    if ($runType -eq 'srilanka' -or $runTypeLower -eq 'restoresl' -or $runTypeLower -eq 'updatesl' -or $runTypeLower -eq 'fixscriptcssl')
    {
        return 'dipscloud.com'
    }

    return (Get-WmiObject Win32_ComputerSystem).Domain
}

if ($runTypeLower -eq 'checkhost')
{
    if (Test-IsBuildServer)
    {
        Remove-ScriptCsPackages
        $runTypeLower = 'restore'
    }
    else
    {
        $runTypeLower = 'update'
    }
}

if ($runTypeLower -eq 'checkhostdb')
{
    if (Test-IsBuildServer)
    {
        Remove-ScriptCsPackages
        $runTypeLower = 'restoredb'
    }
    else
    {
        $runTypeLower = 'updatedb'
    }
}

if ($runTypeLower -eq 'srilanka')
{
    if (Test-IsBuildServer)
    {
        $runTypeLower = 'restoresl'
    }
    else
    {
        $runTypeLower = 'updatesl'
    }
}

$domainName = Get-DomainName -runType $runTypeLower

Write-Caption "Running bootstrapper in '$runTypeLower' mode..."

if ($runTypeLower -eq 'iis')
{
    Enable-IISFeatures
}
elseif ($runTypeLower -eq 'dotnet')
{
    Install-DotNetSDKs
}
elseif ($runTypeLower -ne 'skip')
{
    Assert-PowershellVersion

    Assert-Chocolatey -domainName $domainName

    Assert-NugetCommandLine -domainName $domainName

    if ($runTypeLower -eq 'full' -or $runTypeLower -eq 'update' -or $runTypeLower -eq 'updatesl' -or $runTypeLower -eq 'updatedb')
    {
        Update-Bootstrapper -domainName $domainName

        Import-Module -Name $bootstrapperModule -Force

        Update-Buildsystem -domainName $domainName
    }

    if ($runTypeLower -eq 'restore' -or $runTypeLower -eq 'restoresl' -or $runTypeLower -eq 'restoredb')
    {
        Update-Buildsystem -domainName $domainName -restore $true
    }

    if ($runTypeLower -eq 'full' -or $runTypeLower -eq 'restore' -or $runTypeLower -eq 'restoresl' -or $runTypeLower -eq 'dependencies')
    {
        Assert-ScriptCS -domainName $domainName

        Assert-Ruby -domainName $domainName
        Assert-RubyGems
        Assert-AsciiDoctor
        Assert-AsciiDoctorDiagram
        Assert-AsciiDoctorPdf
        Assert-Coderay

        Assert-Python -domainName $domainName
        Assert-Pip
        Assert-Mkdocs
        Assert-MkdocsMaterial

        Assert-GitCommandline -domainName $domainName
        Assert-GitVersion -domainName $domainName

        Assert-CoverageToXml -domainName $domainName
        Assert-ReportGenerator -domainName $domainName

        Assert-VisualStudioXmlConverter -domainName $domainName

        Assert-PackageManagerCLI -domainName $domainName

        Assert-Pester
    }

    if ($runTypeLower -eq 'updatedb')
    {
        Install-DatabaseBuildTools -domainName $domainName
    }

    if ($runTypeLower -eq 'restoredb')
    {
        Assert-ScriptCS -domainName $domainName

        Assert-GitCommandline -domainName $domainName
        Assert-GitVersion -domainName $domainName

        Assert-DupDesigner -domainName $domainName
        Assert-DupLicense -domainName $domainName
        Assert-DupApi -domainName $domainName
        Assert-DBUpgrade -domainName $domainName
        Assert-DatamartUpgrade -domainName $domainName

        Assert-DatabaseReset -domainName $domainName

        Install-DatabaseBuildTools -domainName $domainName
    }

    if ($runTypeLower -eq 'initialize')
    {
        Update-Buildsystem -domainName $domainName -initialize $true
    }

    if ($runTypeLower -eq 'initializedb')
    {
        Update-Buildsystem -domainName $domainName -initializedb $true
    }

    if ($runTypeLower -eq 'fixscriptcs' -or $runTypeLower -eq 'fixscriptcssl')
    {
        Repair-ScriptCS -domainName $domainName
    }

    Invoke-CustomBootstrapper
}

Install-Dependencies
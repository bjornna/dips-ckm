$ErrorActionPreference = "Stop"

$buildDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rootDir = [System.IO.Path]::GetFullPath((Join-Path $buildDir '..'))

$bootstrapperVersionFile = Join-Path $PSScriptRoot 'bootstrapper-version.txt'
$bootstrapperConfigFile = Join-Path $PSScriptRoot 'bootstrapper.config'
$customBootstrapperScriptPath = Join-Path $PSScriptRoot 'bootstrapper-custom.ps1'
$commonVersionFile = Join-Path $buildDir 'buildsystem-version.txt'
$localVersionFile = Join-Path $buildDir 'buildsystem-version.local'

$buildsystemPackageId = 'dips-buildsystem-common'
$bootstrapperPackageId = 'dips-buildsystem-bootstrapper'

$fileShareUrl = 'http://vd-fileshare01.dips.local'

$minimumChocolateyVersion = '0.10.11'
$nugetCommandLineMinimumVersion = '4.5.1'
$scriptCsMinimumVersion = 'Version: 0.17'
$rubyGemsVersion = '2.7.4'
$rubyVersion = '25'
$rubyChocoVersion = '2.5.3.101'
$minimumPesterVersion = '4.4.0'

$chocolateyPath = Join-Path $Env:ProgramData 'chocolatey'
$chocolateyLibPath = Join-Path $chocolateyPath 'lib'
$chocolateyBinPath = Join-Path $chocolateyPath 'bin'

$rubyPath = "C:\tools\ruby$rubyVersion"
$rubyBinPath = Join-Path $rubyPath 'bin'
$rubyLibPath = Join-Path $rubyPath 'lib'
$rubyGemsPath = Join-Path $rubyLibPath 'ruby\gems'

$tempDir = 'C:\temp'

function Get-ConfigValue([string]$configKey, [string]$defaultValue = $null, [bool]$warnIfMissing = $true)
{
    if (Test-Path $bootstrapperConfigFile)
    {
        $configLines = [IO.File]::ReadAllLines($bootstrapperConfigFile)
        foreach ($line in $configLines)
        {
            $split = $line.Split('=')
            if ($split[0].ToLower() -like $configKey.ToLower())
            {
                $configValue = $split[1]
                Write-Verbose "Config key '$configKey' found with value '$configValue'."
                return $configValue
            }
        }

        if ($warnIfMissing)
        {
            Write-Warning "Could not find key '$configKey' in '$bootstrapperConfigFile', using default value '$defaultValue'."
        }
    }
    else
    {
        Write-Warning "Could not find '$bootstrapperConfigFile', please verify that this file exists."
    }

    return [string]$defaultValue
}

function Get-BoolConfigValue([string]$configKey, [bool]$defaultValue, [bool]$warnIfMissing = $false)
{
    $configValue = Get-ConfigValue -configKey $configKey -defaultValue $null -warnIfMissing $warnIfMissing
    if ([string]::IsNullOrWhiteSpace($configValue))
    {
        return $defaultValue
    }

    $boolValue = $null
    if (-not [bool]::TryParse($configValue, [ref]$boolValue))
    {
        Write-Warning "The configured value for '$configKey' is not a valid bool: '$configValue'. Using default value '$defaultValue'."
        return $defaultValue
    }

    return $boolValue
}

$targetDir = Get-ConfigValue -configKey 'targetDir' -defaultValue $null -warnIfMissing $false
if (-not [string]::IsNullOrEmpty($targetDir))
{
    $commonDir = Join-Path $buildDir $targetDir
    if (-not (Test-Path $commonDir))
    {
        New-Item -Path $commonDir -ItemType Directory
    }
}
else
{
    $commonDir = $buildDir
}

function Write-OK([string]$message)
{
    Write-Host $message -ForegroundColor 'Green'
}

function Write-Caption([string]$message)
{
    Write-Host $message -ForegroundColor 'Cyan'
}

function Assert-RunningAsAdmin
{
    If (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] 'Administrator'))
    {
        return $false
    }

    return $true
}

function Start-ProcessAsAdmin($processName, $arguments)
{
    Start-Process -FilePath $processName -Verb runAs -ArgumentList $arguments -Wait
}

function Get-NugetBaseUrl([string]$domainName)
{
    switch ($domainName.ToLower())
    {
        'dipscloud.com'
        {
            return 'https://dips-nugetsl.dipscloud.com'
        }
        # dips.local, testlab.local, demo.dips.no, etl.dips.no
        Default
        {
            return 'https://dips-nuget'
        }
    }
}

function Get-NugetFeedUrl([string]$domainName, [string]$feedName)
{
    $nugetBaseUrl = Get-NugetBaseUrl -domainName $domainName
    return "$nugetBaseUrl/nuget/$feedName"
}

function Install-ChocolateyPackage([string]$domainName, [string]$packageId, [string]$version, [string]$feedName = 'ExternalSoftware')
{
    $source = Get-NugetFeedUrl -domainName $domainName -feedName $feedName

    if (Assert-RunningAsAdmin)
    {
        if ([string]::IsNullOrWhiteSpace($version))
        {
            & choco install $packageId --source $source --confirm --no-progress --force --prerelease
        }
        else
        {
            & choco install $packageId --version $version --source $source --confirm --no-progress --force --prerelease
        }
    }
    else
    {
        $arguments = "install $packageId --source $source --confirm --no-progress --force --prerelease"
        if (-not [string]::IsNullOrWhiteSpace($version))
        {
            $arguments = "$arguments --version $version"
        }

        Start-ProcessAsAdmin -processName 'choco' -arguments $arguments
    }
}

function Install-NugetPackage([string]$domainName, [string]$packageId, [string]$version = "", [string]$feedName = 'DIPS', [string]$outputDirectory)
{
    $source = Get-NugetFeedUrl -domainName $domainName -feedName $feedName

    if ([string]::IsNullOrWhiteSpace($version))
    {
        nuget install $packageId -source $source -OutputDirectory $outputDirectory -ExcludeVersion -NoCache -NonInteractive
    }
    else
    {
        nuget install $packageId -version $version -source $source -OutputDirectory $outputDirectory -ExcludeVersion -NoCache -NonInteractive
    }
}

function Update-ChocolateyPackage([string]$domainName, [string]$packageId, [string]$feedName = 'ExternalSoftware')
{
    $source = Get-NugetFeedUrl -domainName $domainName -feedName $feedName

    if (Assert-RunningAsAdmin)
    {
        & choco upgrade $packageId --source $source -y --no-progress
    }
    else
    {
        Start-ProcessAsAdmin -processName 'choco' -arguments "upgrade $packageId --source $source -y --no-progress"
    }
}

function Update-ChocolateyPackageLegacy([string]$domainName, [string]$packageId, [string]$feedName = 'ExternalSoftware')
{
    $source = Get-NugetFeedUrl -domainName $domainName -feedName $feedName

    if (Assert-RunningAsAdmin)
    {
        & choco upgrade $packageId -s $source -y
    }
    else
    {
        Start-ProcessAsAdmin -processName 'choco' -arguments "upgrade $packageId -s $source -y"
    }
}

function Install-OnlineChocolateyPackage([string]$packageId)
{
    if (Assert-RunningAsAdmin)
    {
        & choco install $packageId --confirm --no-progress
    }
    else
    {
        Start-ProcessAsAdmin -processName 'choco' -arguments "install $packageId --confirm --no-progress"
    }
}

function Update-Path([switch]$Silent)
{
    if (-not $Silent.IsPresent)
    {
        Write-Warning 'Attempting to reload environment, but if something fails, you may have to close and reopen your shell, and then run this script again.'
    }

    $shell = Get-Shell
    if ($shell -eq 'CMD')
    {
        & refreshenv
    }
    elseif ($shell -eq 'PowerShell')
    {
        Import-Module "$env:ChocolateyInstall\helpers\chocolateyInstaller.psm1" -Force
        Update-SessionEnvironment
    }
    else
    {
        throw "Unknown shell: $shell"
    }
}

function Get-Shell
{
    if (Get-Content 2>&1 -ea ig .)
    {
        return 'CMD'
    }

    return 'PowerShell'
}

function Assert-PowershellVersion
{
    $minimumMajorVersion = 4
    $powershellVersion = $PSVersionTable.PSVersion
    if ($powershellVersion.Major -lt $minimumMajorVersion)
    {
        throw "This script requires at least Powershell version $minimumMajorVersion. Please upgrade Powershell with 'choco install/upgrade powershell', and then rerun this script."
    }

    Write-OK 'Powershell is up to date.'
}

function Assert-Chocolatey([string]$domainName)
{
    $minimumVersion = [System.Version]$minimumChocolateyVersion

    $chocoExePath = Join-Path $chocolateyPath 'choco.exe'
    if (-not (Test-Path $chocoExePath))
    {
        $chocoExePath = Join-Path $chocolateyBinPath 'choco.exe'

        if (-not (Test-Path $chocoExePath))
        {
            Write-Error 'Chocolatey is not installed. Please install chocolatey before running the bootstrapper.'
            Write-Caption 'You can install Chocolatey by running the following command:'
            Write-Caption "iex ((New-Object System.Net.WebClient).DownloadString('https://chocolatey.org/install.ps1'))"
            exit
        }
    }

    Write-OK 'Chocolatey is already installed.'

    $chocolateyVersionString = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($chocoExePath).FileVersion
    $chocolateyVersion = [System.Version]$chocolateyVersionString

    if ($chocolateyVersion -ge $minimumVersion)
    {
        Write-OK "Chocolatey is up to date: $chocolateyVersion"
        return
    }
    else
    {
        Write-Caption "DIPS Buildsystem requires at least version $minimumChocolateyVersion of Chocolatey, but current version is $chocolateyVersionString `nRunning upgrade..."

        if ($chocolateyVersion.Minor -lt 10)
        {
            Update-ChocolateyPackageLegacy -domainName $domainName -packageId 'chocolatey'
        }
        else
        {
            Update-ChocolateyPackage -domainName $domainName -packageId 'chocolatey'
        }

        Update-Path

        Write-OK 'Upgraded Chocolatey.'
    }
}

function Assert-ScriptCS([string]$domainName)
{
    try
    {
        $scriptcsVersion = & scriptcs -v | Out-String

        Write-OK 'ScriptCS is already installed.'

        if ($scriptcsVersion -match $scriptCsMinimumVersion)
        {
            Write-OK "ScriptCS is up to date: `n$scriptcsVersion"
            return
        }
        else
        {
            Write-Caption "DIPS Buildsystem requires version 0.17.x of ScriptCS, but current version is `n$scriptcsVersion `nRunning upgrade..."
        }
    }
    catch
    {
        Write-Caption 'ScriptCS is not installed, installing...'
    }

    Update-ChocolateyPackage -domainName $domainName -packageId 'scriptcs'

    Write-OK 'Installed ScriptCS.'

    Repair-ScriptCS -domainName $domainName
}

function Repair-ScriptCS([string]$domainName)
{
    if (-not (Assert-RunningAsAdmin))
    {
        throw "You must run this script with administrator privileges."
    }

    $packageId = 'Microsoft.Web.Xdt'
    $version = '2.1.1'

    $packageDir = Join-Path $tempDir $packageId
    if (Test-Path $packageDir)
    {
        Remove-Item $packageDir -Recurse -Force
    }

    Install-NugetPackage -domainName $domainName -packageId $packageId -version $version -feedName '3rdParty' -outputDirectory $packageDir

    $fileName = 'Microsoft.Web.XmlTransform.dll'

    $sourcePath = [System.IO.Path]::Combine($packageDir, $packageId, 'lib', 'net40', $fileName)
    $destinationPath = [System.IO.Path]::Combine($chocolateyLibPath, 'scriptcs', 'tools', $fileName)

    Copy-Item -Path $sourcePath -Destination $destinationPath -Force
}

function Assert-NugetCommandLine([string]$domainName)
{
    try
    {
        $nugetHelp = & nuget ?
        $nugetVersionLine = ($nugetHelp -split '\n')[0]
        $nugetVersionString = $nugetVersionLine -replace 'NuGet Version: ',''

        $nugetVersion = [System.Version]$nugetVersionString
        $minimumVersion = [System.Version]$nugetCommandLineMinimumVersion

        if ($nugetVersion -ge $minimumVersion)
        {
            Write-OK "NuGet.CommandLine is up to date: $nugetVersion"
            return
        }

        Write-Caption 'NuGet.CommandLine is outdated, updating...'

        Update-ChocolateyPackage -domainName $domainName -packageId 'NuGet.CommandLine'

        Write-OK 'Updated NuGet.CommandLine.'
    }
    catch
    {
        Write-Caption 'NuGet.CommandLine is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'NuGet.CommandLine'

        Write-OK 'Installed NuGet.CommandLine.'
    }
}

function Assert-Ruby([string]$domainName)
{
    try
    {
        $rubyVersion = & ruby -v
        Write-OK "Ruby is already installed: $rubyVersion"
    }
    catch
    {
        Write-Caption 'Ruby is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'Ruby' -version $rubyChocoVersion

        Update-Path

        $path = [Environment]::GetEnvironmentVariable('PATH', 'machine')
        if (-not ($path -like "*$rubyBinPath*"))
        {
            [Environment]::SetEnvironmentVariable('PATH', "$rubyBinPath;$path", 'machine')
        }

        Write-OK 'Installed Ruby.'
    }
}

function Assert-RubyGems
{
    $minimumVersion = [System.Version]$rubyGemsVersion
    try
    {
        $rubyGemsVersionOutput = & gem --version
        $rubyGemsVersion = [System.Version]$rubyGemsVersionOutput

        Write-OK "rubygems is installed: $rubyGemsVersionOutput"

        if ($rubyGemsVersion -ge $minimumVersion)
        {
            Write-OK "rubygems is up to date with the required minimum version ($minimumVersion)."
            return
        }
        else
        {
            Write-Caption 'rubygems is outdated, updating...'
            Write-Warning 'If you keep seeing this update, you may have 2 different versions of Ruby installed. Try uninstalling both, and then run this again.'

            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

            $rubyGems = Join-Path $tempDir "rubygems-update-$rubyGemsVersion.gem"
            Invoke-WebRequest "https://rubygems.org/downloads/rubygems-update-$rubyGemsVersion.gem" -OutFile $rubyGems -UseBasicParsing

            try
            {
                & gem install --local $rubyGems
                & gem update --system
            }
            catch
            {
                Write-Error 'Could not find rubygems on the command line. If Ruby/rubygems has been installed, you may have to close and reopen your shell.'
                throw
            }

            Remove-Item $rubyGems

            Write-OK 'Updated rubygems.'
        }
    }
    catch
    {
        Write-Caption 'rubygems is not installed, installing...'

        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

        $rubyGemsZip = Join-Path $tempDir "rubygems-$rubyGemsVersion.zip"
        Invoke-WebRequest "https://rubygems.org/rubygems/rubygems-$rubyGemsVersion.zip" -OutFile $rubyGemsZip -UseBasicParsing

        $rubyGems = Join-Path $tempDir 'rubygems'
        if (Test-Path $rubyGems)
        {
            Remove-Item $rubyGems -Recurse -Force
        }

        try
        {
            Add-Type -AssemblyName 'System.IO.Compression.FileSystem'
            [System.IO.Compression.ZipFile]::ExtractToDirectory($rubyGemsZip, $rubyGems)
        }
        catch
        {
            Write-Error 'Unable to unzip rubygems. Try closing and reopening your shell, and then run this again.'
            throw
        }

        $currentLocation = Get-Location
        Set-Location $rubyGems

        if (Assert-RunningAsAdmin)
        {
            & ruby setup.rb
        }
        else
        {
            Start-ProcessAsAdmin -processName 'ruby' -arguments 'setup.rb'
        }

        Set-Location $currentLocation

        Update-Path

        Write-OK 'Installed rubygems.'
    }
}

function Assert-AsciiDoctor
{
    $gemName = 'asciidoctor'
    $installedVersionString = Get-InstalledGemVersion -gemName $gemName
    $latestVersionString = Get-LatestGemVersion -gemName $gemName

    if ([string]::IsNullOrEmpty($latestVersionstring))
    {
        Write-Warning "Unable to determine latest available version of $gemName."
        return
    }

    $latestVersion = [System.Version]$latestVersionString

    if ([string]::IsNullOrEmpty($installedVersionString))
    {
        Write-Caption "$gemName is not installed, installing..."

        Install-Gem -gemName $gemName

        Write-OK "Installed $gemName."
    }
    else
    {
        $installedVersion = [System.Version]$installedVersionString
        if ($installedVersion -ge $latestVersion)
        {
            Write-Ok "$gemName is up to date: $latestVersionString"
        }
        else
        {
            Write-Caption "$gemName is outdated, updating..."

            Update-Gem -gemName $gemName

            Write-Ok "Updated $gemName to version $latestVersionString."
        }
    }

    if ($latestVersion -lt ([System.Version]'2.0.0'))
    {
        Repair-Asciidoctor -gemName $gemName
    }
}

function Repair-Asciidoctor([string]$gemName)
{
    $fileName = 'path_resolver.rb'
    $tempPath = Join-Path 'C:\temp' $fileName

    if (-not (Test-Path $tempPath))
    {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

        Invoke-WebRequest "https://raw.githubusercontent.com/mojavelinux/asciidoctor/75a6fea540784ee58aba77f8ac9d2acd1487c7b6/lib/asciidoctor/$fileName" -OutFile $tempPath -UseBasicParsing
    }

    $asciidoctorPath = Find-GemPath -gemName $gemName
    $asciidoctorLibPath = Join-Path $asciidoctorPath 'lib'
    $asciidoctorLibFilesPath = Join-Path $asciidoctorLibPath $gemName
    $destinationPath = Join-Path $asciidoctorLibFilesPath $fileName

    $newContent = Get-Content $tempPath
    $oldContent = Get-Content $destinationPath
    if ($newContent.Length -ne $oldContent.Length)
    {
        Write-Warning 'Applying asciidoctor path resolver hotfix...'
        Copy-Item -Path $tempPath -Destination $destinationPath -Force
    }
}

function Find-GemPath([string]$gemName)
{
    $rubyVersionPath = Find-RubyVersion
    $gemsDirectory = Join-Path $rubyVersionPath 'gems'

    $gemDirectory = Get-ChildItem -Path $gemsDirectory -Directory | Where-Object { $_.Name -match "$gemName-[0-9]\.[0-9]\.[0-9]" } | Select-Object -Last 1
    Join-Path $gemsDirectory $gemDirectory | Write-Output
}

function Find-RubyVersion
{
    $rubyVersion = Get-ChildItem -Path $rubyGemsPath -Directory | Select-Object -Last 1
    Join-Path $rubyGemsPath $rubyVersion | Write-Output
}

function Assert-AsciiDoctorDiagram
{
    $installedVersion = Get-InstalledGemVersion -gemName 'asciidoctor-diagram'
    if ([string]::IsNullOrEmpty($installedVersion))
    {
        Write-Caption 'asciidoctor-diagram is not installed, installing...'

        Install-Gem -gemName 'asciidoctor-diagram'

        Write-OK 'Installed asciidoctor-diagram.'
    }
}

function Assert-AsciiDoctorPdf
{
    $installedVersion = Get-InstalledGemVersion -gemName 'asciidoctor-pdf'
    if ([string]::IsNullOrEmpty($installedVersion))
    {
        Write-Caption 'asciidoctor-pdf is not installed, installing...'

        Install-Gem -gemName 'asciidoctor-pdf' -prerelease $true

        Write-OK 'Installed asciidoctor-pdf.'
    }
}

function Assert-Coderay
{
    $installedVersion = Get-InstalledGemVersion -gemName 'coderay'
    if ([string]::IsNullOrEmpty($installedVersion))
    {
        Write-Caption 'coderay is not installed, installing...'

        Install-Gem -gemName 'coderay'

        Write-OK 'Installed coderay.'
    }
}

function Get-InstalledGemVersion([string]$gemName)
{
    $result = & gem list $gemName --exact --local

    if ([string]::IsNullOrWhiteSpace($result))
    {
        Write-Output $null
        return
    }

    $versionMatch = [Regex]::Match($result, "$gemName \((.*)\)")
    if ($versionMatch.Success)
    {
        $versionSplit = $versionMatch.Groups[1].Value -split ' '
        $installedVersion = $versionSplit[0].Trim(',')
        Write-OK "$gemName is installed: $installedVersion"
        Write-Output $installedVersion
    }
}

function Install-Gem([string]$gemName, [bool]$prerelease = $false)
{
    if ($prerelease)
    {
        & gem install $gemName --pre
    }
    else
    {
        & gem install $gemName
    }
}

function Update-Gem([string]$gemName, [bool]$prerelease = $false)
{
    if ($prerelease)
    {
        & gem update $gemName
    }
    else
    {
        & gem update $gemName
    }
}

function Get-LatestGemVersion([string]$gemName, [bool]$prerelease = $false)
{
    if ($prerelease)
    {
        $result = & gem list $gemName --exact --remote --pre
    }
    else
    {
        $result = & gem list $gemName --exact --remote
    }

    if ([string]::IsNullOrWhiteSpace($result))
    {
        Write-Output $null
        return
    }

    $versionMatch = [Regex]::Match($result, "$gemName \(([0-9]+\.[0-9]+\.[0-9]*).*\)")
    if ($versionMatch.Success)
    {
        Write-Output $versionMatch.Groups[1].Value
    }
}

function Assert-Python([string]$domainName)
{
    $skipMkdocs = Get-BoolConfigValue -configKey 'skipMkdocs' -defaultValue $true -warnIfMissing $false
    if ($skipMkdocs)
    {
        return
    }

    try
    {
        $pythonVersion = & python --version
        Write-OK "Python is installed: $pythonVersion"
    }
    catch
    {
        Write-Caption 'Python is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'Python'

        Update-Path

        Write-OK 'Installed Python.'
    }
}

function Assert-Pip
{
    $skipMkdocs = Get-BoolConfigValue -configKey 'skipMkdocs' -defaultValue $true -warnIfMissing $false
    if ($skipMkdocs)
    {
        return
    }

    try
    {
        $pipVersion = & pip --version
        Write-OK "pip is installed: $pipVersion"
    }
    catch
    {
        Write-Caption 'pip is not installed, installing...'
        Write-Warning 'If you keep seeing this update, you may have 2 different versions of Python installed. Try uninstalling both, and then run this again.'

        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

        $getPip = Join-Path $tempDir 'get-pip.py'
        Invoke-WebRequest 'https://bootstrap.pypa.io/get-pip.py' -OutFile $getPip -UseBasicParsing

        try
        {
            & python $getPip
        }
        catch
        {
            Write-Error 'Could not find python on the command line. If Python has been installed, you may have to close and reopen your shell.'
            throw
        }

        Write-OK 'Installed pip.'
    }
}

function Assert-Mkdocs
{
    $skipMkdocs = Get-BoolConfigValue -configKey 'skipMkdocs' -defaultValue $true -warnIfMissing $false
    if ($skipMkdocs)
    {
        return
    }

    try
    {
        $mkdocsVersion = & mkdocs --version
        Write-OK "mkdocs is installed: $mkdocsVersion"
    }
    catch
    {
        Write-Caption 'mkdocs is not installed, installing...'

        & pip install mkdocs

        Write-OK 'Installed mkdocs.'
    }
}

function Assert-MkdocsMaterial
{
    $skipMkdocs = Get-BoolConfigValue -configKey 'skipMkdocs' -defaultValue $true -warnIfMissing $false
    if ($skipMkdocs)
    {
        return
    }

    $pipPackages = & pip list --format=columns --disable-pip-version-check
    if ($pipPackages -match 'mkdocs-material')
    {
        Write-OK 'mkdocs-material is installed.'
    }
    else
    {
        Write-Caption 'mkdocs-material is not installed, installing...'

        & pip install mkdocs-material
        & pip install pygments

        Update-Path

        Write-OK 'Installed mkdocs-material.'
    }
}

function Assert-GitCommandline([string]$domainName)
{
    try
    {
        $gitVersion = & git --version
        Write-OK "Git command line is installed: $gitVersion"
    }
    catch
    {
        Write-Caption 'Git command line is not installed, installing...'
        Write-Warning 'You may have to close any open ssh shell sessions!'

        Install-ChocolateyPackage -domainName $domainName -packageId 'git.install'

        Write-OK 'Installed Git command line.'
    }
}

function Assert-GitVersion([string]$domainName)
{
    try
    {
        $gitVersion = & gitversion help
        Write-OK 'GitVersion is installed.'
    }
    catch
    {
        Write-Caption 'GitVersion is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'GitVersion.Portable'

        Write-OK 'Installed GitVersion.'
    }
}

function Assert-CoverageToXml([string]$domainName)
{
    $coverageToXmlExePath = Join-Path $chocolateyLibPath 'CoverageToXml\CoverageToXml.exe'
    if (Test-Path $coverageToXmlExePath)
    {
        Write-OK 'CoverageToXml is installed.'
    }
    else
    {
        Write-Caption 'CoverageToXml is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'CoverageToXml'

        Write-OK 'Installed CoverageToXml.'
    }
}

function Assert-VisualStudioXmlConverter([string]$domainName)
{
    $coverageToXmlExePath = Join-Path $chocolateyLibPath 'VisualStudioXmlConverter\DIPS.VisualStudioXmlConverterApp.exe'
    if (Test-Path $coverageToXmlExePath)
    {
        Write-OK 'VisualStudioXmlConverter is installed.'
    }
    else
    {
        Write-Caption 'VisualStudioXmlConverter is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'VisualStudioXmlConverter'

        Write-OK 'Installed VisualStudioXmlConverter.'
    }
}

function Assert-ChocoPackage([string]$packageid)
{
	$output = choco search $packageid -localonly

	($output -match $packageid)
}

function Assert-DupDesigner([string]$domainName)
{
    New-PSDrive -Name TempHKCR -PSProvider Registry -Root HKEY_CLASSES_ROOT

    [bool]$DupDesignerInstalled = $false

    if(Test-Path "TempHKCR:DupSoft AS.Dup Designer")
    {
        $DupDesignerInstalled = $true
    }

    Remove-PSDrive TempHKCR

	if(-not ($DupDesignerInstalled))
	{
        Write-Caption 'Dup-Designer is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'dup-designer' -feedName 'InternalSoftware'

        Write-OK 'Installed Dup-Designer.'
    }
    else
    {
        Write-OK 'Dup-Designer is installed.'
    }
}

function Assert-DupApi([string]$domainName)
{
	if(-not (Assert-ChocoPackage -packageid 'dup-api'))
	{
        Write-Caption 'dup-api is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'dup-api' -feedName 'InternalSoftware'

        Write-OK 'Installed dup-api.'
    }
    else
    {
        Write-OK 'dup-api is installed.'
    }
}

function Assert-DatabaseReset([string]$domainName)
{
	if(-not (Assert-ChocoPackage -packageid 'dips-databasereset'))
	{
        Write-Caption 'dips-databasereset is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'dips-databasereset' -feedName 'InternalSoftware'

        Write-OK 'Installed dips-databasereset.'
    }
    else
    {
        Write-OK 'dips-databasereset is installed.'
    }
}


function Assert-DupLicense([string]$domainName)
{
	if(-not (Assert-ChocoPackage -packageid 'dupsoft-dips-license'))
	{
        Write-Caption 'dupsoft-dips-license is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'dupsoft-dips-license' -feedName 'InternalSoftware'

        Write-OK 'Installed dupsoft-dips-license.'
    }
    else
    {
        Write-OK 'dupsoft-dips-license is installed.'
    }
}

function Assert-DBUpgrade([string]$domainName)
{
	if(-not (Assert-ChocoPackage -packageid 'dips-dbupgrade'))
	{
        Write-Caption 'dips-dbupgrade is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'dips-dbupgrade' -feedName 'DIPS-Release'

        Write-OK 'Installed dips-dbupgrade.'
    }
    else
    {
        Write-OK 'dips-dbupgrade is installed.'
    }
}

function Assert-DatamartUpgrade([string]$domainName)
{
	if(-not (Assert-ChocoPackage -packageid 'dips-datamartupgrade'))
	{
        Write-Caption 'dips-datamartupgrade is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'dips-datamartupgrade' -feedName 'InternalSoftware'

        Write-OK 'Installed dips-datamartupgrade.'
    }
    else
    {
        Write-OK 'dips-datamartupgrade is installed.'
    }
}

function Assert-ReportGenerator([string]$domainName)
{
    try
    {
        $reportGeneratorDoc = & ReportGenerator
        Write-OK 'ReportGenerator is installed.'
    }
    catch
    {
        Write-Caption 'ReportGenerator is not installed, installing...'

        Install-ChocolateyPackage -domainName $domainName -packageId 'ReportGenerator'

        Write-OK 'Installed ReportGenerator.'
    }
}

function Get-InstalledModuleVersions([string]$moduleName)
{
    $installedVersion = Get-Module -ListAvailable -Name $moduleName | Select-Object -ExpandProperty 'version'
    Write-Output $installedVersion
}

function Assert-Pester([string]$domainName)
{
    $minVersion = [System.Version]$minimumPesterVersion
    $installedVersions = Get-InstalledModuleVersions -moduleName 'Pester'
    foreach ($installedVersion in $installedVersions)
    {
        if ([System.Version]$installedVersion -ge $minimumPesterVersion)
        {
            Write-OK "Pester is up to date: $installedVersion"
            return
        }
    }

    Write-Caption 'Updating Pester...'

    Update-ChocolateyPackage -domainName $domainName -packageId 'Pester'
    Update-Path

    Write-OK 'Updated Pester.'
}

function Assert-PackageManagerCLI([string]$domainName)
{
    try
    {
        $packageManagerCliVersion = & pm.exe --version

        $versionPattern = '[0-9]*\.[0-9]*\.[0-9]'
        if ($packageManagerCliVersion -match $versionPattern)
        {
            Write-OK "$packageManagerCliVersion is installed."
            return
        }
    }
    catch
    {
    }

    Write-Caption 'PackageManager CLI is not installed, installing...'

    Install-ChocolateyPackage -domainName $domainName -packageId 'dips-arena-packagemanager-cli' -feedName 'InternalSoftware'

    Write-OK 'Installed PackageManager CLI.'
}

function Update-Bootstrapper([string]$domainName, [bool]$restore = $false)
{
    $currentBootstrapperVersion = Get-CurrentVersion -currentVersionFile $bootstrapperVersionFile
    $allowPrerelease = Get-BoolConfigValue -configKey 'allowPrerelease' -defaultValue $false

    $upgradeVersion = Test-IsUpgradeAvailable -domainName $domainName -packageId $bootstrapperPackageId -currentVersion $currentBootstrapperVersion -allowPrerelease $allowPrerelease

    if (-not [string]::IsNullOrEmpty($upgradeVersion) -and -not $restore)
    {
        Write-Caption "An update is available for the bootstrapper. Current version is $currentBootstrapperVersion, latest version is $upgradeVersion."

        Install-NugetPackage -domainName $domainName -packageId $bootstrapperPackageId -version $upgradeVersion -feedName 'InternalSoftware' -outputDirectory $PSScriptRoot

        $buildsystemBootstrapperDir = Join-Path $PSScriptRoot $bootstrapperPackageId

        $upgradeScriptPath = Join-Path $buildsystemBootstrapperDir 'upgrade-bootstrapper.ps1'

        & $upgradeScriptPath -bootstrapperDir $PSScriptRoot

        Remove-Item -Path $buildsystemBootstrapperDir -Recurse -Force

        Write-VersionToFile -versionFile $bootstrapperVersionFile -version $upgradeVersion
        Write-OK "Updated the bootstrapper to version $upgradeVersion."
    }
    else
    {
        Write-OK "The bootstrapper is up to date."
    }
}

function Update-Buildsystem([string]$domainName, [bool]$initialize = $false, [bool]$restore = $false, [bool]$initializedb = $false)
{
    $currentCommonVersion = Get-CurrentVersion -currentVersionFile $commonVersionFile
    $allowPrerelease = Get-BoolConfigValue -configKey 'allowPrerelease' -defaultValue $false

    $upgradeVersion = Test-IsUpgradeAvailable -domainName $domainName -packageId $buildsystemPackageId -currentVersion $currentCommonVersion -allowPrerelease $allowPrerelease
    if (-not $restore -and ($null -eq $upgradeVersion))
    {
        $restore = Test-ShouldRestoreBuildsystem -commonVersion $currentCommonVersion
    }

    if ($initialize)
    {
        Update-CommonFiles -domainName $domainName -version $upgradeVersion -initialize $initialize

        Write-VersionToFile -versionFile $commonVersionFile -version $upgradeVersion
        Write-VersionToFile -versionFile $localVersionFile -version $upgradeVersion

        Write-GitIgnore

        Write-OK 'Initialized buildsystem. Please make appropriate changes to build.csx.'
    }
    elseif ($initializedb)
    {
        Update-CommonFiles -domainName $domainName -version $upgradeVersion -initializedb $initializedb

        Write-VersionToFile -versionFile $commonVersionFile -version $upgradeVersion
        Write-VersionToFile -versionFile $localVersionFile -version $upgradeVersion

        Write-OK 'Initialized buildsystem for database build projects. Please make appropriate changes to databasebuild.csx.'
    }
    elseif (-not [string]::IsNullOrWhiteSpace($upgradeVersion) -and -not $restore)
    {
        Write-Caption "An update is available for DIPS BuildSystem. Current version is $currentCommonVersion, latest version is $upgradeVersion."

        Update-CommonFiles -domainName $domainName -version $upgradeVersion

        Write-VersionToFile -versionFile $commonVersionFile -version $upgradeVersion
        Write-VersionToFile -versionFile $localVersionFile -version $upgradeVersion

        Write-OK "Updated DIPS BuildSystem to version $upgradeVersion."
    }
    elseif ($restore)
    {
        Write-Caption "Restoring buildsystem to version $currentCommonVersion"
        Update-CommonFiles -domainName $domainName -version $currentCommonVersion

        Write-VersionToFile -versionFile $localVersionFile -version $currentCommonVersion

        Write-OK "Restored buildsystem to version $currentCommonVersion."
    }
    else
    {
        Write-OK "DIPS BuildSystem is up to date."
    }
}

function Get-CurrentVersion([string]$currentVersionFile)
{
    $currentVersion = '0.0.0'
    if (Test-Path $currentVersionFile)
    {
        $currentVersion = [IO.File]::ReadAllText($currentVersionFile)
    }

    return $currentVersion
}

function Test-IsUpgradeAvailable(
    [string]$domainName,
    [string]$packageId,
    [string]$currentVersion,
    [string]$sourceName = 'InternalSoftware',
    [bool]$allowPrerelease = $false)
{
    $upgradeMajor = Get-BoolConfigValue -configKey 'upgradeMajor' -defaultValue $false

    $latestVersion = Get-LatestVersionOfPackage -domainName $domainName -packageId $packageId -allowPrerelease $allowPrerelease -sourceName $sourceName

    if ([System.String]::IsNullOrWhiteSpace($currentVersion))
    {
        return $latestVersion
    }

    $current = ConvertTo-SimplifiedVersion $currentVersion
    $latest = ConvertTo-SimplifiedVersion $latestVersion

    if (-not $upgradeMajor -and $latest.Major -gt $current.Major)
    {
        Write-Warning "A new MAJOR version exists for package $packageId, but upgradeMajor is $upgradeMajor."

        $allVersions = Get-AllVersionsOfPackage -domainName $domainName -packageId $packageId -sourceName $sourceName
        $matchingMajorVersions = $allVersions | Where-Object { (ConvertTo-SimplifiedVersion $_).Major -eq $current.Major }
        if ($matchingMajorVersions.Count -gt 0)
        {
            $latestVersion = ($matchingMajorVersions | Measure-Object -Maximum).Maximum
            $latest = [System.Version]$latestVersion
        }
        else
        {
            $latest = ConvertTo-SimplifiedVersion ''
        }
    }

    if ($latest -gt $current)
    {
        return $latestVersion
    }

    return $null
}

function Test-ShouldRestoreBuildsystem([string]$commonVersion)
{
    if ([System.String]::IsNullOrWhiteSpace($commonVersion))
    {
        return $false
    }

    $alwaysRestore = Get-BoolConfigValue -configKey 'alwaysRestore' -defaultValue $false -warnIfMissing $false
    if ($alwaysRestore)
    {
        return $true
    }

    if (-not (Test-Path (Join-Path $commonDir 'common.csx')))
    {
        return $true
    }

    if (-not (Test-Path $localVersionFile))
    {
        return $false
    }

    $localCommonVersion = Get-CurrentVersion -currentVersionFile $localVersionFile

    $common = ConvertTo-SimplifiedVersion $commonVersion
    $localCommon = ConvertTo-SimplifiedVersion $localCommonVersion

    if ($common -ne $localCommon)
    {
        return $true
    }

    return $false
}

function ConvertTo-SimplifiedVersion([string]$version)
{
    if ([string]::IsNullOrWhiteSpace($version))
    {
        return [System.Version]'0.0.0'
    }

    $simplifiedVersion = $version -replace "[A-z]?",""
    $simplifiedVersion = $simplifiedVersion -replace "-","."

    return [System.Version]$simplifiedVersion
}

function Get-LatestVersionOfPackage([string]$domainName, [string]$packageId, [bool]$allowPrerelease, [string]$sourceName = 'InternalSoftware')
{
    $nugetBaseUrl = Get-NugetBaseUrl -domainName $domainName
    $source = "$nugetBaseUrl/nuget/$sourceName"

    if ($allowPrerelease)
    {
        $result = & nuget list $packageId -source $source -prerelease
    }
    else
    {
        $result = & nuget list $packageId -source $source
    }

    if ([string]::IsNullOrWhiteSpace($result) -or $result -like 'No packages*')
    {
        throw "Could not find $packageId at $source"
    }

    $match = $result -match "^$packageId\s"
    if ($match -eq $true)
    {
        $version = ($result -replace $packageId).Trim()
    }
    elseif ($null -ne $match)
    {
        $version = ($match -replace $packageId).Trim()
    }
    else
    {
        throw "Could not parse version output for package '$packageId': '$result'"
    }

    return $version
}

function Get-AllVersionsOfPackage([string]$domainName, [string]$packageId, [bool]$allowPrerelease, [string]$sourceName = 'InternalSoftware')
{
    $nugetBaseUrl = Get-NugetBaseUrl -domainName $domainName
    $source = "$nugetBaseUrl/nuget/$sourceName"

    if ($allowPrerelease)
    {
        $result = & nuget list $packageId -source $source -prerelease -allversions
    }
    else
    {
        $result = & nuget list $packageId -source $source -allversions
    }

    if ([string]::IsNullOrWhiteSpace($result) -or $result -like 'No packages*')
    {
        throw "Could not find $packageId at $source"
    }

    $versions = ($result -replace $packageId).Split(' ')

    return $versions
}

function Update-CommonFiles([string]$domainName, [string]$version, [bool]$initialize = $false, [bool]$initializedb = $false)
{
    Install-NugetPackage -domainName $domainName -packageId $buildsystemPackageId -version $version -feedName 'InternalSoftware' -outputDirectory $PSScriptRoot

    $buildsystemCommonDir = Join-Path $PSScriptRoot $buildsystemPackageId

    $upgradeScriptPath = Join-Path $buildsystemCommonDir 'upgrade\upgrade-common.ps1'
    $copyTemplates = Get-BoolConfigValue -configKey 'copyTemplates' -defaultValue $initialize
    $updateBuildwindowBat = Get-BoolConfigValue -configKey 'updateBuildwindowBat' -defaultValue $initialize
    $updateScriptcsPackagesConfig = Get-BoolConfigValue -configKey 'updateScriptcsPackagesConfig' -defaultValue $true
	if ($domainName.ToLower() -eq 'dipscloud.com')
	{
		$updateScriptcsPackagesConfig = $false
	}
	
    & $upgradeScriptPath -rootDir $rootDir -buildDir $buildDir -commonDir $commonDir -copyTemplates $copyTemplates -updateBuildwindowBat $updateBuildwindowBat -updateScriptcsPackagesConfig $updateScriptcsPackagesConfig -initialize $initialize -initializedb $initializedb
	
    Remove-Item -Path $buildsystemCommonDir -Recurse -Force
}

function Write-VersionToFile([string]$versionFile, [string]$version)
{
    Write-Caption "Writing version $version to $versionFile..."
    [IO.File]::WriteAllText([string]$versionFile, [string]$version)
}

function Write-GitIgnore
{
    $gitIgnoreContent = [System.Collections.ArrayList]@()

    $gitIgnorePath = Join-Path $rootDir '.gitignore'
    if (Test-Path $gitIgnorePath)
    {
        $gitIgnoreContent.AddRange([IO.File]::ReadAllLines($gitIgnorePath))
    }

    $gitIgnoreContent.Add('build/common/')
    $gitIgnoreContent.Add('scriptcs_packages/')
    $gitIgnoreContent.Add('buildsystem-version.local')

    [IO.File]::WriteAllLines($gitIgnorePath, $gitIgnoreContent)
}

function Install-Dependencies
{
    Write-Caption 'Installing all ScriptCS Buildsystem dependencies...'
    $currentDir = Get-Location
    Set-Location $buildDir
    & scriptcs -install
    Set-Location $currentDir
    Write-OK 'Finished installing ScriptCS Buildsystem dependencies.'
}

function Install-DatabaseBuildTools([string]$domainName)
{
	$dbtoolsDir = "$buildDir\dbbuildtools"

    if(Test-Path $dbtoolsDir)
    {
        Remove-Item $dbtoolsDir -Recurse -Force
    }
    $feedName = "DIPS"
    if ($domainName.ToLower() -eq "dipscloud.com") {
        $feedName = "InternalNugetTools"
    }

    Install-NugetPackage -domainName $domainName -packageId 'dips-reset_database' -feedName $feedName -OutputDirectory $dbtoolsDir
    Install-NugetPackage -domainName $domainName -packageId 'dips.dup_powertools' -feedName $feedName -OutputDirectory $dbtoolsDir
    Install-NugetPackage -domainName $domainName -packageId 'dips.data_dictionary_tests' -feedName $feedName -OutputDirectory $dbtoolsDir
    Install-NugetPackage -domainName $domainName -packageId 'plsqldev-test-runner' -feedName $feedName -OutputDirectory $dbtoolsDir
}

function Test-IsBuildServer
{
    # check if we are running on a build server
    $buildServerEnvVar = $Env:TEAMCITY_VERSION
    if (-not [string]::IsNullOrEmpty($buildServerEnvVar))
    {
        return $true
    }

    $buildServerEnvVar = $Env:TF_BUILD
    if (-not [string]::IsNullOrEmpty($buildServerEnvVar))
    {
        return $true
    }

    return $false
}

function Invoke-CustomBootstrapper
{
    if (Test-Path $customBootstrapperScriptPath)
    {
        Write-Host 'Running custom bootstrapper script...'
        & $customBootstrapperScriptPath
    }
    else
    {
        Write-Host 'No custom bootstrapper script found.'
    }
}

function Enable-IISFeatures
{
    Write-Caption 'Enabling IIS features...'

    Enable-WindowsOptionalFeature -FeatureName 'IIS-WebServer' -Online -All
    Enable-WindowsOptionalFeature -FeatureName 'IIS-CommonHttpFeatures' -Online -All
    Enable-WindowsOptionalFeature -FeatureName 'IIS-ApplicationDevelopment' -Online -All
    Enable-WindowsOptionalFeature -FeatureName 'IIS-NetFxExtensibility45' -Online -All
    Enable-WindowsOptionalFeature -FeatureName 'IIS-ASPNET' -Online -All
    Enable-WindowsOptionalFeature -FeatureName 'IIS-ASPNET45' -Online -All
    Enable-WindowsOptionalFeature -FeatureName 'IIS-ClientCertificateMappingAuthentication' -Online -All
    Enable-WindowsOptionalFeature -FeatureName 'WCF-HTTP-Activation' -Online -All

    Install-OnlineChocolateyPackage -packageId 'urlrewrite'
    Install-OnlineChocolateyPackage -packageId 'iis-arr'
    
    Enable-WindowsOptionalFeature -FeatureName 'IIS-HttpRedirect' -Online -All

    Set-WebConfigurationProperty -pspath 'MACHINE/WEBROOT/APPHOST' -filter "system.webServer/proxy" -name "enabled" -value "True"
}

function Install-DotNetSDKs
{
    Write-Caption 'Installing .NET SDKs...'

    Install-OnlineChocolateyPackage -packageId 'netfx-4.6.2-devpack'
    Install-OnlineChocolateyPackage -packageId 'netfx-4.7.1-devpack'
    Install-OnlineChocolateyPackage -packageId 'dotnetcore-sdk'
    Install-OnlineChocolateyPackage -packageId 'dotnetcore-windowshosting'
}

function Remove-ScriptCsPackages()
{
    $packagePath = Join-Path $buildDir "scriptcs_packages"
    if (Test-Path $packagePath)
    {
        Remove-Item $packagePath -Recurse -Force
    }
}
function Install-Pm([string]$domainName, [string]$packageId)
{
    $nugetBaseUrl = Get-NugetBaseUrl -domainName $domainName
    $chocoFeed = "$nugetBaseUrl/nuget/DIPS-Dev"

    if (Assert-RunningAsAdmin)
    {
        & choco upgrade $packageId --source $chocoFeed -y --no-progress --force
    }
    else
    {
        Start-ProcessAsAdmin -processName 'choco' -arguments "upgrade $packageId --source $chocoFeed -y --no-progress --force"
    }
}

Install-Pm (Get-WmiObject Win32_ComputerSystem).Domain "dips-arena-packagemanager-cli"
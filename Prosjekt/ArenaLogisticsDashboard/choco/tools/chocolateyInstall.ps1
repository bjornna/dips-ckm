function Exec([scriptblock]$cmd, [string]$errorMessage = "Error executing command: " + $cmd) {
    & $cmd
    if ($LastExitCode -ne 0) {
        throw $errorMessage
    }
}

Import-Module DIPSChoco

$arguments = Get-InstallParameters

$hostname = "http://localhost"
$user = "user"
$password = "password"
$userRole = 0
$timeout = 30

if ($arguments) {
    if ($arguments.ContainsKey("host")) {
        $hostname = $arguments["host"]
    }
    
    if ($arguments.ContainsKey("user")) {
        $user = $arguments["user"]
    }
    
    if ($arguments.ContainsKey("password")) {
        $password = $arguments["password"]
    }    
    
    if ($arguments.ContainsKey("userrole")) {
        $userRole = $arguments["userrole"]
    }    

    if ($arguments.ContainsKey("timeout")) {
        $timeout = $arguments["timeout"]
    } 
}

Write-Host "host:       $hostname"
Write-Host "user:       $user"
Write-Host "password:   $password"
Write-Host "userrole:   $userrole"
Write-Host "timeout:    $timeout"

$sourceDir = "$PSScriptRoot\.."

Get-ChildItem -Path "$sourceDir\*.zip" | ForEach-Object {
    exec { & pm install $_.FullName --host $hostname --user $user --password $password --userrole $userRole --timeout $timeout -f } 
}

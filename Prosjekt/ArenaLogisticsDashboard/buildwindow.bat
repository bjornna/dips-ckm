@echo off

title "%~dp0"

:: Determine if called interactively or from cmd.exe
set interactive=1
echo %cmdcmdline% | find /i "%~0" >nul
if not errorlevel 1 set interactive=0

:: set correct working dir when running as admin
cd /D "%~dp0"

:: filter --skipboot from params and change runtype if specified
set skipbootstrapper="--skipboot"
set runtype="checkhost"
set params=
set var=0
:nextparam
set /A var=%var% + 1
for /F "tokens=%var% delims= " %%A in ("%*") do (
    if "%%~A"==%skipbootstrapper% set runtype="skip"

    if not "%%~A"==%skipbootstrapper% set "params=%params%%%A "
    goto nextparam
)

PowerShell -NoProfile -ExecutionPolicy Bypass -Command "& .\build\bootstrapper\bootstrapper.ps1 -runtype '%runtype%'"

title %~dp0

setlocal
cd build
scriptcs -LogLevel info -ScriptName build.csx -- %params%

IF %ERRORLEVEL% NEQ 0 (
    echo Buildscript exited with errorcode %errorlevel%
    if _%interactive%_==_0_ pause
    exit /b %errorlevel%
)
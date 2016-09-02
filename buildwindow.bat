@echo off
:: Determine if called interactively or from cmd.exe
set interactive=1
echo %cmdcmdline% | find /i "%~0" >nul
if not errorlevel 1 set interactive=0

:: Set size of command window if we are running interactively
:: if _%interactive%_==_0_ mode con: cols=200 lines=80
:: Install scriptcs and run build script
cinst scriptcs -y  1> nul
setlocal
cd build
scriptcs build.csx -- %*

IF %ERRORLEVEL% NEQ 0 (
    echo Buildscript exited with errorcode %errorlevel%
    if _%interactive%_==_0_ pause
    exit /b %errorlevel%
)
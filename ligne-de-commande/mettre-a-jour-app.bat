@echo off
rem ===========================================================================
rem  Recompile l'application et remplace EmuWorks.exe
rem
rem  Windows verrouille un .exe en cours d'execution : ferme la fenetre de
rem  l'emulateur avant de lancer ce script.
rem ===========================================================================
setlocal
set "BASE=%~dp0..\"
set "PUB=%BASE%app\bin\Release\net8.0-windows\win-x64\publish\EmuWorks.exe"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo Le SDK .NET est requis :  winget install Microsoft.DotNet.SDK.8
  pause
  exit /b 1
)

echo Compilation...
dotnet publish "%BASE%app\EmuWorks.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true --nologo -v q
if errorlevel 1 (
  echo Echec de la compilation.
  pause
  exit /b 1
)

copy /y "%PUB%" "%BASE%EmuWorks.exe" >nul
if errorlevel 1 (
  echo.
  echo Impossible de remplacer EmuWorks.exe : il est probablement ouvert.
  echo Ferme la fenetre de l'emulateur et relance ce script.
  pause
  exit /b 1
)

echo EmuWorks.exe est a jour.
timeout /t 4 >nul

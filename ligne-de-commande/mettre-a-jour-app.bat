@echo off
rem ===========================================================================
rem  Recompile l'application et remplace C:\NumWorks\NumWorks.exe
rem
rem  Windows verrouille un .exe en cours d'execution : ferme la fenetre de
rem  l'emulateur avant de lancer ce script.
rem ===========================================================================
setlocal
set "BASE=%~dp0..\"
set "PUB=%BASE%app\bin\Release\net8.0-windows\win-x64\publish\NumWorks.exe"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo Le SDK .NET est requis :  winget install Microsoft.DotNet.SDK.8
  pause
  exit /b 1
)

echo Compilation...
dotnet publish "%BASE%app\NumWorks.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true --nologo -v q
if errorlevel 1 (
  echo Echec de la compilation.
  pause
  exit /b 1
)

copy /y "%PUB%" "%BASE%NumWorks.exe" >nul
if errorlevel 1 (
  echo.
  echo Impossible de remplacer NumWorks.exe : il est probablement ouvert.
  echo Ferme la fenetre de l'emulateur et relance ce script.
  pause
  exit /b 1
)

echo NumWorks.exe est a jour.
timeout /t 4 >nul

@echo off
rem ===========================================================================
rem  Choisit le firmware que la calculatrice emulee execute.
rem
rem    firmware.bat           liste ce qui est disponible
rem    firmware.bat <nom>     installe firmwares\<nom>\ dans rom\
rem
rem  Les scripts Python de rom\scripts\ sont conserves : c'est le meme dossier
rem  qui suit d'un firmware a l'autre.
rem ===========================================================================
setlocal
set "BASE=%~dp0..\"
set "FW=%BASE%firmwares"

if "%~1"=="" (
  echo Firmwares disponibles :
  for /d %%d in ("%FW%\*") do echo    %%~nxd
  echo.
  echo Usage : firmware.bat ^<nom^>
  exit /b 0
)

if not exist "%FW%\%~1\internal.bin" (
  echo Firmware inconnu : %~1
  echo Regarde dans %FW%
  exit /b 1
)

copy /y "%FW%\%~1\internal.bin" "%BASE%rom\internal.bin" >nul
copy /y "%FW%\%~1\external.bin" "%BASE%rom\external.bin" >nul

rem  L'adresse du stockage change d'un firmware a l'autre : on jette l'image de
rem  reference, NumWorks.bat la reconstruira au prochain demarrage.
if exist "%BASE%rom\sram.bin"       del "%BASE%rom\sram.bin"
if exist "%BASE%rom\.storage.bin"   del "%BASE%rom\.storage.bin"
if exist "%BASE%rom\.storage.json"  del "%BASE%rom\.storage.json"
if exist "%BASE%rom\load.resc"      del "%BASE%rom\load.resc"

echo %~1 installe. Lance NumWorks.bat.

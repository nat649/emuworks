@echo off
set "EMUWORKS_BASE=%~dp0..\"
rem ===========================================================================
rem  Emulateur puce NumWorks N0110 (Renode)
rem    run.bat        -> restaure la sauvegarde (flash.bin) si elle existe
rem    run.bat neuf   -> ignore la sauvegarde et repart d'un firmware vierge
rem
rem  Dans le moniteur : 'start' pour demarrer, 'runMacro $sauver' pour sauver.
rem ===========================================================================

set "RENODE=C:\Program Files\Renode\bin\Renode.exe"
set "SAVE=%~dp0..\flash.bin"

if not exist "%RENODE%" (
  echo Renode introuvable : %RENODE%
  echo Installe-le avec :  winget install Renode.Renode
  pause
  exit /b 1
)

if /i "%~1"=="neuf" (
  if exist "%SAVE%" del "%SAVE%"
)

cd /d "%~dp0..\renode"

if exist "%SAVE%" (
  echo Restauration de la sauvegarde...
  "%RENODE%" -e "i @numworks_n0110.resc" -e "runMacro $restaurer" -e "start"
) else (
  "%RENODE%" numworks_n0110.resc
)

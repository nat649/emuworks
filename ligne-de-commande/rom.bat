@echo off
rem ===========================================================================
rem  Synchronise le dossier C:\NumWorks\rom\ avec la calculatrice emulee.
rem
rem    rom.bat pull   apres 'runMacro $tirer' dans Renode
rem                   -> remplit rom\scripts\ avec les .py de la calculatrice
rem
rem    rom.bat push   apres avoir ajoute/modifie des .py dans rom\scripts\
rem                   -> prepare l'image ; puis 'runMacro $pousser' dans Renode
rem ===========================================================================

set "ROM=%~dp0..\rom"
set "TOOLS=%~dp0..\renode\tools"

if /i "%~1"=="pull" (
  if not exist "%ROM%\sram.bin" (
    echo Pas de vidage SRAM. Dans Renode, fais d'abord :  runMacro $tirer
    exit /b 1
  )
  node "%TOOLS%\rom.js" pull "%ROM%\sram.bin" "%ROM%"
  exit /b %errorlevel%
)

if /i "%~1"=="push" (
  node "%TOOLS%\rom.js" push "%ROM%"
  exit /b %errorlevel%
)

echo Usage : rom.bat pull ^| push
exit /b 1

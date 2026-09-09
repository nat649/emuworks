@echo off
rem ===========================================================================
rem  Emulateur NumWorks N0110 - lanceur unique
rem
rem    - demarre la calculatrice a partir du dossier rom\
rem    - y injecte les scripts Python de rom\scripts\
rem    - enregistre tout dans rom\scripts\ quand tu fermes la fenetre
rem
rem  Rien d'autre a taper : double-clic suffit.
rem ===========================================================================
setlocal
title Emulateur NumWorks

set "BASE=%~dp0..\"
set "RENODE=C:\Program Files\Renode\bin\Renode.exe"
set "ROM=%BASE%rom"
set "TOOLS=%BASE%renode\tools"

if not exist "%RENODE%" (
  echo Renode est introuvable : %RENODE%
  echo Installe-le avec :  winget install Renode.Renode
  pause
  exit /b 1
)

where node >nul 2>&1
if errorlevel 1 (
  echo Node.js est requis pour synchroniser les scripts Python.
  echo Installe-le avec :  winget install OpenJS.NodeJS.LTS
  pause
  exit /b 1
)

if not exist "%ROM%\internal.bin" (
  echo Firmware absent : %ROM%\internal.bin
  echo Choisis-en un avec :  firmware.bat
  pause
  exit /b 1
)

rem --- filet de securite : copie des scripts avant toute synchronisation ----
if exist "%ROM%\scripts" (
  if exist "%ROM%\.scripts-precedents" rd /s /q "%ROM%\.scripts-precedents"
  xcopy "%ROM%\scripts" "%ROM%\.scripts-precedents\" /e /i /q >nul 2>&1
)

echo Demarrage de la calculatrice...
cd /d "%BASE%renode"
"%RENODE%" numworks.resc

rem --- Renode est ferme : le dernier vidage devient des fichiers .py --------
echo.
if exist "%ROM%\sram.bin" (
  echo Enregistrement dans %ROM%\scripts ...
  node "%TOOLS%\rom.js" pull "%ROM%\sram.bin" "%ROM%"
) else (
  echo Aucun vidage memoire : rien a enregistrer.
)

echo.
echo Termine.
timeout /t 6 >nul

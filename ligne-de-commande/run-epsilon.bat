@echo off
rem ===========================================================================
rem  Lance l'emulateur avec EPSILON OFFICIEL 15.5.0
rem  (le firmware par defaut, Omega 2.0.2, se lance avec run.bat)
rem ===========================================================================

set "RENODE=C:\Program Files\Renode\bin\Renode.exe"

if not exist "C:\NumWorks\epsilon-officiel\epsilon.onboarding.internal.bin" (
  echo Firmware Epsilon officiel absent de C:\NumWorks\epsilon-officiel\
  echo Compile-le avec le workflow "Firmware N0110 ^(Renode^)" sur ton fork.
  pause
  exit /b 1
)

cd /d "%~dp0..\renode"
"%RENODE%" epsilon_officiel.resc

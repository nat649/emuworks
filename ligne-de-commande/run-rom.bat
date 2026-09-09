@echo off
set "EMUWORKS_BASE=%~dp0..\"
rem  Lance l'emulateur sur le dossier rom\
set "RENODE=C:\Program Files\Renode\bin\Renode.exe"
cd /d "%~dp0..\renode"
"%RENODE%" rom.resc

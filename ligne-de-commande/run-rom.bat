@echo off
rem  Lance l'emulateur sur le dossier C:\NumWorks\rom\
set "RENODE=C:\Program Files\Renode\bin\Renode.exe"
cd /d "%~dp0..\renode"
"%RENODE%" rom.resc

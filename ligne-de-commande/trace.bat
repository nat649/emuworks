@echo off
rem ===========================================================================
rem  Test de non-regression du boot -> C:\NumWorks\trace.txt
rem  A relancer apres CHAQUE modification du .repl ou des .cs
rem ===========================================================================

cd /d "%~dp0..\renode"
set "RENODE=C:\Program Files\Renode\bin\Renode.exe"

echo Boot en cours (environ 30 s)...
"%RENODE%" --console --disable-xwt -e "i @boottest.resc" -e "sysbus LogAllPeripheralsAccess true" -e "emulation RunFor \"0.6\"" -e "cpu PC" -e "quit" > "%~dp0..\trace.txt" 2>&1

echo.
echo Trace ecrite dans %~dp0..\trace.txt

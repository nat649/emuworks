@echo off
set "EMUWORKS_BASE=%~dp0..\"
rem ===========================================================================
rem  Compare le boot de deux firmwares dans le meme emulateur :
rem    Omega 2.0.2          -> trace-omega.txt
rem    Epsilon officiel     -> trace-epsilon.txt
rem  Les deux tournent 0.6 s de temps simule, avec le journal des peripheriques.
rem ===========================================================================

cd /d "%~dp0..\renode"
set "RENODE=C:\Program Files\Renode\bin\Renode.exe"

echo [1/2] Omega 2.0.2...
"%RENODE%" --console --disable-xwt -e "i @boottest.resc" -e "sysbus LogAllPeripheralsAccess true" -e "emulation RunFor \"0.6\"" -e "cpu PC" -e "lcd Stats" -e "quit" > "%~dp0..\trace-omega.txt" 2>&1

echo [2/2] Epsilon officiel 15.5.0...
"%RENODE%" --console --disable-xwt -e "i @boottest-epsilon.resc" -e "sysbus LogAllPeripheralsAccess true" -e "emulation RunFor \"0.6\"" -e "cpu PC" -e "lcd Stats" -e "quit" > "%~dp0..\trace-epsilon.txt" 2>&1

echo.
echo Traces ecrites : %~dp0..\trace-omega.txt  et  %~dp0..\trace-epsilon.txt

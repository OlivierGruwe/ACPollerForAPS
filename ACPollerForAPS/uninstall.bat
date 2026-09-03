@echo off
REM Desinstalle le service. A lancer en tant qu'administrateur.

set INSTALLUTIL=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe
set EXE=%~dp0ACPollerForAPS.Service.exe

sc stop ACPollerForAPS
"%INSTALLUTIL%" /u "%EXE%"
pause
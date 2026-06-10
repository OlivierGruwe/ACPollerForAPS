@echo off
set INSTALLUTIL=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe
set EXE=%~dp0ACTxt2Xml.exe

"%INSTALLUTIL%" "%EXE%"
echo.
sc start ACTxt2Xml
pause
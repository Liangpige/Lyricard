@echo off
rem Self-test for src\LayoutEngine.cs (step 3 of the modular rewrite).
rem Compiles the two model files plus the harness; nothing else is linked.
rem Exit code 0 = every rectangle matches the numbers the shipping card reports.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=%TEMP%\layout-selftest.exe
"%CSC%" -nologo -codepage:65001 -out:"%OUT%" "%~dp0..\src\CardConfig.cs" "%~dp0..\src\LayoutEngine.cs" "%~dp0layout-selftest.cs"
if errorlevel 1 exit /b 1
"%OUT%"

@echo off
rem Round-trip test for src\CardConfig.cs. Compiles the model together with the
rem test harness (nothing else is linked) and runs it. Exit code 0 = all good.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=%TEMP%\cfg-roundtrip.exe
"%CSC%" -nologo -codepage:65001 -out:"%OUT%" "%~dp0..\src\CardConfig.cs" "%~dp0cfg-roundtrip.cs"
if errorlevel 1 exit /b 1
"%OUT%"

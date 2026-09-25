@echo off
setlocal

call "%~dp0generate_packets.bat" --no-pause
if errorlevel 1 exit /b 1

call "%~dp0generate_auth_contracts.bat"
if errorlevel 1 exit /b 1

exit /b 0
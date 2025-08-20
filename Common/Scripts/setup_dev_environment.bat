@echo off
setlocal

:: ========================================
:: 개발 환경 설정 스크립트
:: 용도: 서버 개발에 필요한 모든 설정 자동화
:: ========================================

echo ========================================
echo    C# MMORPG Server Development Setup
echo ========================================
echo.

:: 현재 스크립트 위치에서 프로젝트 루트 계산
set SCRIPT_DIR=%~dp0
set PROJECT_ROOT=%SCRIPT_DIR%\..\..\

echo [1/4] Copying GameData files...
call "%SCRIPT_DIR%copy_gamedata.bat" auto

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Failed to copy GameData files
    pause
    exit /b 1
)

echo.
echo [2/4] Building Server solution...
cd /d "%PROJECT_ROOT%\Server"
dotnet build Server.sln --configuration Debug

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Server build failed
    pause
    exit /b 1
)

echo.
echo [3/4] Building DummyClient...
dotnet build DummyClient/DummyClient.csproj --configuration Debug

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] DummyClient build failed
    pause
    exit /b 1
)

echo.
echo [4/4] Development environment setup complete!
echo.
echo ========================================
echo    Ready to develop!
echo ========================================
echo.
echo Available commands:
echo   - Start Server: cd Server/Server ^&^& dotnet run
echo   - Start Client: cd Server/DummyClient ^&^& dotnet run
echo   - Copy GameData: Common/Scripts/copy_gamedata.bat
echo   - Generate Packets: cd Common/Protobuf ^&^& generate_packets.bat
echo.
echo [NOTE] Remember to run copy_gamedata.bat after modifying JSON files
echo.

pause
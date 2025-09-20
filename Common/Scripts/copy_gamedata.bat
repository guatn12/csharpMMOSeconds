@echo off
setlocal

:: ========================================
:: GameData 복사 배치 스크립트
:: 용도: Common/GameData 폴더를 서버 실행 위치로 복사
:: ========================================

echo [GameData Copy Script] Starting...

:: 현재 스크립트 위치에서 프로젝트 루트 계산
set SCRIPT_DIR=%~dp0
set PROJECT_ROOT=%SCRIPT_DIR%\..\

:: 소스 및 대상 경로 설정 (Common/GameData로 변경)
set SOURCE_DIR=%PROJECT_ROOT%\GameData
set TARGET_DIR=%PROJECT_ROOT%\Server\Server\bin\Debug\net8.0\GameData

echo [INFO] Source Directory: %SOURCE_DIR%
echo [INFO] Target Directory: %TARGET_DIR%

:: 소스 디렉토리 존재 확인
if not exist "%SOURCE_DIR%" (
    echo [ERROR] Source GameData directory not found: %SOURCE_DIR%
    echo [ERROR] Please check if GameData folder exists in Common/
    pause
    exit /b 1
)

:: 대상 디렉토리 생성 (없으면)
if not exist "%TARGET_DIR%" (
    echo [INFO] Creating target directory: %TARGET_DIR%
    mkdir "%TARGET_DIR%"
)

:: JSON 파일들 복사
echo [INFO] Copying JSON files...
copy "%SOURCE_DIR%\*.json" "%TARGET_DIR%\" /Y

if %ERRORLEVEL% EQU 0 (
    echo [SUCCESS] GameData files copied successfully!
    echo [SUCCESS] Files copied to: %TARGET_DIR%
    
    :: 복사된 파일 목록 표시
    echo.
    echo [INFO] Copied files:
    dir "%TARGET_DIR%\*.json" /B
) else (
    echo [ERROR] Failed to copy GameData files. Error code: %ERRORLEVEL%
    pause
    exit /b 1
)

echo.
echo [COMPLETE] GameData copy operation finished.
echo [NOTE] Run this script after each GameData modification.
echo [NOTE] Source: Common/GameData/*.json
echo.

:: 개발 환경에서는 자동으로 닫기, 수동 실행시에는 대기
if "%1"=="auto" (
    exit /b 0
) else (
    echo Press any key to continue...
    pause >nul
)
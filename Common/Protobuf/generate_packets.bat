@echo off
REM ===================================================
REM Packet Code Generation Script
REM ===================================================
ECHO Starting Packet Code Generation...
REM ECHO.

REM --- 경로 설정 ---
REM 배치 파일이 위치한 상대경로 솔루션 루트 디렉토리로 이동합니다.
SET SOLUTION_DIR=%~dp0..\

REM --- 주요 경로 설정 ---
SET PROTOC_DIR=%SOLUTION_DIR%protoc\bin
SET PROTO_PATH=%SOLUTION_DIR%Protobuf\
SET PROTO_FILE=%SOLUTION_DIR%Protobuf\Protocol.proto

REM --- 서버와 클라이언트 프로젝트 경로 설정 ---
SET SERVER_PACKET_DEST_DIR=%SOLUTION_DIR%..\Server\Server\Packet
SET SERVER_PACKET_HANDLER_DEST_DIR=%SERVER_PACKET_DEST_DIR%\Handlers
SET SERVER_PACKET_HANDLER_GEN_DEST_DIR=%SERVER_PACKET_HANDLER_DEST_DIR%\GenerateHandlers
SET DUMMYCLIENT_PACKET_DEST_DIR=%SOLUTION_DIR%..\Server\DummyClient\Packet
SET CLIENT_GENERATED_DEST_DIR=%SOLUTION_DIR%Generated

REM --- PacketGenerator 경로 설정 ---
SET GENERATOR_PROJECT_EXE=%SOLUTION_DIR%..\Server\PacketGenerate\bin\Debug\net8.0\PacketGenerate.exe
SET GENERATOR_PROJECT=%SOLUTION_DIR%..\Server\PacketGenerate\PacketGenerate.csproj
REM PacketGenerator가 생성한 파일들 경로 (배치 파일 실행 디렉토리)
SET GENERATOR_OUTPUT_DIR=%~dp0

REM ===================================================
REM 1. Protobuf 메시지 클래스 생성 (protoc.exe 사용)
REM ===================================================
REM ECHO [1/3] Generating Protobuf message classes using protoc.exe...

REM 공통과 클라이언트와 서버에서 사용할 C# 메시지 클래스 생성
REM (생성된 Protocol.cs 파일은 공통과 클라이언트 프로젝트 참조용으로 사용)
%PROTOC_DIR%\protoc.exe -I=%PROTO_PATH% --csharp_out=%PROTO_PATH% %PROTO_FILE%
IF ERRORLEVEL 1 PAUSE

REM ECHO  -> C# message classes generated in Common\Generated.
REM ECHO.

REM ===================================================
REM 2. 커스텀 코드 생성 (PacketGenerator.exe 사용)
REM ===================================================
REM ECHO [2/3] Generating custom helper code with PacketGenerator...
REM
REM REM PacketGenerator 프로젝트를 빌드하여 Generated 폴더에 파일 생성
dotnet run --project %GENERATOR_PROJECT% -- %PROTO_FILE%
REM START ../../Server/PacketGenerate/bin/Debug/net8.0/PacketGenerate.exe ./Protocol.proto
IF ERRORLEVEL 1 PAUSE
REM
REM ECHO  -> Custom helper codes generated in PacketGenerate\bin\Debug\net8.0\Generated.
REM ECHO.
timeout /t 2 /nobreak > nul
REM ===================================================
REM 3. 생성된 커스텀 코드를 최종경로에 복사
REM ===================================================
REM ECHO [3/3] Copying generated files to final destinations...
REM
REM REM 서버용 파일들 복사
REM 기본 패킷 파일 복사
xcopy /Y /Q Gen_Server_PacketID.cs "%SERVER_PACKET_DEST_DIR%"
xcopy /Y /Q Gen_Server_PacketManager.cs "%SERVER_PACKET_DEST_DIR%"

REM IPacketHandler.cs는 Handlers 디렉토리로 복사 (수동 핸들러와 같은 위치)
xcopy /Y /Q IPacketHandler.cs "%SERVER_PACKET_HANDLER_GEN_DEST_DIR%"

REM GenerateHandlers 하위 디렉토리 생성 (없으면)
if not exist "%SERVER_PACKET_HANDLER_GEN_DEST_DIR%" mkdir "%SERVER_PACKET_HANDLER_GEN_DEST_DIR%"

REM 자동 생성된 Category 핸들러 파일들을 GenerateHandlers로 복사 (동적 처리)
ECHO Copying generated handler files to GenerateHandlers...
for %%f in ("%GENERATOR_OUTPUT_DIR%\*PacketHandler.Generated.cs") do (
    xcopy /Y /Q "%%f" "%SERVER_PACKET_HANDLER_GEN_DEST_DIR%\" > nul
    ECHO  - %%~nxf copied to GenerateHandlers
)

REM ECHO  -> Server files copied.
REM
REM REM 더미 클라이언트용 파일들 복사
xcopy /Y /Q Gen_Client_PacketID.cs "%DUMMYCLIENT_PACKET_DEST_DIR%"
xcopy /Y /Q Gen_Client_PacketManager.cs "%DUMMYCLIENT_PACKET_DEST_DIR%"
REM ECHO  -> Client files copied.
REM ECHO.
REM
REM REM 클라이언트용 파일들 복사
xcopy /Y /Q Gen_Client_PacketID.cs "%CLIENT_GENERATED_DEST_DIR%"
xcopy /Y /Q Gen_Client_PacketManager.cs "%CLIENT_GENERATED_DEST_DIR%"
xcopy /Y /Q Gen_PacketID.h "%CLIENT_GENERATED_DEST_DIR%"
xcopy /Y /Q Protocol.cs "%CLIENT_GENERATED_DEST_DIR%"
REM ECHO  -> Client files copied.
REM ECHO.

REM ===================================================
ECHO All packet codes generated and copied successfully!
REM ECHO.
pause

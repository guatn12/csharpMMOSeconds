@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
for %%i in ("%SCRIPT_DIR%..\..") do set "REPO_ROOT=%%~fi"

set "GENERATOR_PROJECT=%REPO_ROOT%\Server\AuthContractGenerate\AuthContractGenerate.csproj"
set "STAGING_ROOT=%REPO_ROOT%\Server\AuthContractGenerate\obj\ContractGeneration"
set "CLIENT_STAGE=%STAGING_ROOT%\Client"
set "SERVER_STAGE=%STAGING_ROOT%\Server"
set "GAME_DEST=%REPO_ROOT%\Server\Server\Services\Auth\Generated"
set "AUTH_DEST=%REPO_ROOT%\Server\AuthServer\Grpc\Generated"

if not exist "%GENERATOR_PROJECT%" exit /b 1

if exist "%STAGING_ROOT%" rmdir /s /q "%STAGING_ROOT%"
mkdir "%CLIENT_STAGE%" || exit /b 1
mkdir "%SERVER_STAGE%" || exit /b 1

dotnet restore "%GENERATOR_PROJECT%" || exit /b 1

dotnet build "%GENERATOR_PROJECT%" --no-restore ^
  -p:AuthContractRole=Client ^
  -p:AuthContractOutputDir="%CLIENT_STAGE%" || exit /b 1

dotnet build "%GENERATOR_PROJECT%" --no-restore ^
  -p:AuthContractRole=Server ^
  -p:AuthContractOutputDir="%SERVER_STAGE%" || exit /b 1

for %%f in (
  "%CLIENT_STAGE%\AuthInternal.cs"
  "%CLIENT_STAGE%\AuthInternalGrpc.cs"
  "%SERVER_STAGE%\AuthInternal.cs"
  "%SERVER_STAGE%\AuthInternalGrpc.cs"
) do if not exist "%%~f" exit /b 1

findstr /C:"AuthInternalClient" "%CLIENT_STAGE%\AuthInternalGrpc.cs" >nul || exit /b 1
findstr /C:"AuthInternalBase" "%CLIENT_STAGE%\AuthInternalGrpc.cs" >nul && exit /b 1
findstr /C:"AuthInternalBase" "%SERVER_STAGE%\AuthInternalGrpc.cs" >nul || exit /b 1
findstr /C:"AuthInternalClient" "%SERVER_STAGE%\AuthInternalGrpc.cs" >nul && exit /b 1

if not exist "%GAME_DEST%" mkdir "%GAME_DEST%" || exit /b 1
if not exist "%AUTH_DEST%" mkdir "%AUTH_DEST%" || exit /b 1

copy /y "%CLIENT_STAGE%\AuthInternal.cs" "%GAME_DEST%\AuthInternal.cs" >nul || exit /b 1
copy /y "%CLIENT_STAGE%\AuthInternalGrpc.cs" "%GAME_DEST%\AuthInternalGrpc.cs" >nul || exit /b 1
copy /y "%SERVER_STAGE%\AuthInternal.cs" "%AUTH_DEST%\AuthInternal.cs" >nul || exit /b 1
copy /y "%SERVER_STAGE%\AuthInternalGrpc.cs" "%AUTH_DEST%\AuthInternalGrpc.cs" >nul || exit /b 1

exit /b 0
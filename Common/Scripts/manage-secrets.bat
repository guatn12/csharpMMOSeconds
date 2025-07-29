  @echo off
  setlocal

  set PROJECT_PATH=..\..\Server\Server

  echo === MMO Server User Secrets 관리 도구 ===
  echo.
  echo 1. User Secrets 초기화
  echo 2. 개발용 민감정보 설정
  echo 3. 현재 설정 조회
  echo 4. 특정 키 설정
  echo 5. 특정 키 삭제
  echo Q. 종료
  echo.
  set /p choice="선택하세요 (1-5, Q): "

  if "%choice%"=="1" goto init_secrets
  if "%choice%"=="2" goto set_dev_secrets
  if "%choice%"=="3" goto list_secrets
  if "%choice%"=="4" goto set_key
  if "%choice%"=="5" goto remove_key
  if /i "%choice%"=="Q" goto end

  :init_secrets
  echo User Secrets 초기화 중...
  dotnet user-secrets init --project %PROJECT_PATH%
  echo 완료!
  pause
  goto start

  :set_dev_secrets
  echo 개발용 민감정보 설정 중...
  dotnet user-secrets set "ServerConfiguration:Security:EncryptionKey" "dev-encryption-key-64-chars-minimum-for-testing123456!@#$789Test" --project %PROJECT_PATH%
  dotnet user-secrets set "ServerConfiguration:Security:TokenSecret" "dev-jwt-secret-key-for-authentication-testing-only12345!@#$6789Te" --project %PROJECT_PATH%
  dotnet user-secrets set "ServerConfiguration:Database:ConnectionString" "Server=localhost;Database=MMOGameDB_Dev;Integrated Security=true;" --project %PROJECT_PATH%
  echo 개발용 설정 완료!
  pause
  goto start

  :list_secrets
  echo 현재 User Secrets 설정:
  dotnet user-secrets list --project %PROJECT_PATH%
  pause
  goto start

  :set_key
  set /p key="설정할 키를 입력하세요: "
  set /p value="값을 입력하세요: "
  dotnet user-secrets set "%key%" "%value%" --project %PROJECT_PATH%
  echo 설정 완료!
  pause
  goto start

  :remove_key
  set /p key="삭제할 키를 입력하세요: "
  dotnet user-secrets remove "%key%" --project %PROJECT_PATH%
  echo 삭제 완료!
  pause
  goto start

  :end
  endlocal
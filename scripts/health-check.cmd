@echo off
setlocal EnableExtensions

set "REPO_ROOT=%~dp0.."
pushd "%REPO_ROOT%" >nul
if errorlevel 1 (
  echo [FAIL] Could not switch to repo root: "%REPO_ROOT%"
  exit /b 1
)

echo [%date% %time%] Stopping DemoStudio app processes...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\stop-dev.ps1"
if errorlevel 1 (
  echo [FAIL] stop-dev failed.
  popd >nul
  exit /b 1
)

echo [%date% %time%] Stopping testhost/vstest processes...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\stop-testhost.ps1"
if errorlevel 1 (
  echo [FAIL] stop-testhost failed.
  popd >nul
  exit /b 1
)

echo [%date% %time%] Building solution...
set "BUILD_OK=0"
for /L %%I in (1,1,3) do (
  echo [%date% %time%] Build attempt %%I/3...
  dotnet build DemoStudio.Desktop.sln -c Release -nologo
  if not errorlevel 1 (
    set "BUILD_OK=1"
    goto :BuildDone
  )
  if %%I LSS 3 (
    echo [%date% %time%] Build attempt %%I failed. Retrying in 2 seconds...
    timeout /t 2 /nobreak >nul
  )
)

:BuildDone
if "%BUILD_OK%" NEQ "1" (
  echo [FAIL] Build failed after 3 attempts.
  popd >nul
  exit /b 1
)

echo [%date% %time%] Running tests...
dotnet test tests\DemoStudio.Desktop.Core.Tests\DemoStudio.Desktop.Core.Tests.csproj -c Release --no-build -nologo
if errorlevel 1 (
  echo [FAIL] Desktop core tests failed.
  popd >nul
  exit /b 1
)

dotnet test tests\DemoStudio.Desktop.App.Tests\DemoStudio.Desktop.App.Tests.csproj -c Release --no-build -nologo
if errorlevel 1 (
  echo [FAIL] Tests failed.
  popd >nul
  exit /b 1
)

echo [%date% %time%] [PASS] Health check complete.
popd >nul
exit /b 0

@echo off
setlocal EnableExtensions

set "SITE_ROOT=%~dp0"
set "PAGES_BASE=/Windows-Server-Setupper"
set "PAGES_BASE_SET=0"
set "RUN_TESTS=1"

:parse_arguments
if "%~1"=="" goto :arguments_ready
if /I "%~1"=="/ci" (
  set "RUN_TESTS=0"
  shift
  goto :parse_arguments
)
if /I "%~1"=="--no-tests" (
  set "RUN_TESTS=0"
  shift
  goto :parse_arguments
)
if "%PAGES_BASE_SET%"=="1" (
  echo Unexpected argument: %~1
  echo Usage: build.bat [/ci ^| --no-tests] [/repository-base-path]
  exit /b 2
)
set "PAGES_BASE=%~1"
set "PAGES_BASE_SET=1"
shift
goto :parse_arguments

:arguments_ready
if "%RUN_TESTS%"=="1" (
  set "TOTAL_STEPS=4"
) else (
  set "TOTAL_STEPS=3"
)

echo [1/%TOTAL_STEPS%] Restoring the exact documentation dependencies with npm ci...
pushd "%SITE_ROOT%" || exit /b 1
call npm ci
if errorlevel 1 goto :failure

echo [2/%TOTAL_STEPS%] Building the Cloudflare Worker-compatible Sites output...
call npm run build
if errorlevel 1 goto :failure

echo [3/%TOTAL_STEPS%] Exporting the same rendered site for GitHub Pages at %PAGES_BASE%/...
call npm run export:pages -- --base-path "%PAGES_BASE%"
if errorlevel 1 goto :failure

if "%RUN_TESTS%"=="1" (
  echo [4/4] Running the focused site contract checks...
  call node --test tests/*.test.mjs
  if errorlevel 1 goto :failure
) else (
  echo Local site contract tests deliberately skipped because /ci or --no-tests was supplied.
)

echo Sites output: %SITE_ROOT%dist
echo GitHub Pages output: %SITE_ROOT%pages-dist
echo GitHub Pages base path: %PAGES_BASE%/
popd
exit /b 0

:failure
set "BUILD_EXIT=%ERRORLEVEL%"
echo Documentation build failed with exit code %BUILD_EXIT%.
popd
exit /b %BUILD_EXIT%

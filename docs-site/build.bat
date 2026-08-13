@echo off
setlocal EnableExtensions

set "SITE_ROOT=%~dp0"
set "PAGES_BASE=/Windows-Server-Setupper"
if not "%~1"=="" set "PAGES_BASE=%~1"

echo [1/4] Restoring the exact documentation dependencies with npm ci...
pushd "%SITE_ROOT%" || exit /b 1
call npm ci
if errorlevel 1 goto :failure

echo [2/4] Building the Cloudflare Worker-compatible Sites output...
call npm run build
if errorlevel 1 goto :failure

echo [3/4] Exporting the same rendered site for GitHub Pages at %PAGES_BASE%/...
call npm run export:pages -- --base-path "%PAGES_BASE%"
if errorlevel 1 goto :failure

echo [4/4] Running the focused site contract checks...
call node --test tests/*.test.mjs
if errorlevel 1 goto :failure

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

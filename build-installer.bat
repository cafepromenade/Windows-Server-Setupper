@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "PROJECT_OUTPUT=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Release"
set "PROJECT_EXE=%PROJECT_OUTPUT%\Windows-Server-Tools.exe"
set "COMMIT_FILE=%PROJECT_OUTPUT%\source-commit.txt"
set "BUILD_HASH_FILE=%PROJECT_OUTPUT%\source-executable.sha256"
set "GENERATED_REFERENCE_CACHE=Windows-Server-Tools/Windows-Server-Tools/obj/Release/Windows-Server-Tools.csproj.AssemblyReference.cache"
set "SCRIPT=%ROOT%packaging\WindowsServerTools.iss"
set "INSTALLER_DIR=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Installer"
set "SILENT_MODE=0"

if /i "%SILENT%"=="1" set "SILENT_MODE=1"
for %%A in (%*) do (
    if /i "%%~A"=="/s" set "SILENT_MODE=1"
    if /i "%%~A"=="--silent" set "SILENT_MODE=1"
)

pushd "%ROOT%" >nul || (
    echo ERROR: Could not enter the repository root: "%ROOT%".
    exit /b 1
)

echo [1/6] Recording the exact source commit...
where git.exe >nul 2>&1 || (
    echo ERROR: Missing dependency: Git is required to identify the installer source commit.
    popd >nul
    exit /b 1
)
for /f "delims=" %%I in ('git rev-parse HEAD 2^>nul') do set "SOURCE_COMMIT=%%I"
if not defined SOURCE_COMMIT (
    echo ERROR: Could not resolve the current Git commit.
    popd >nul
    exit /b 1
)
git diff --quiet --ignore-submodules -- . ":(exclude)%GENERATED_REFERENCE_CACHE%"
if errorlevel 1 (
    echo ERROR: Tracked files differ from commit %SOURCE_COMMIT%. Commit the intended source before packaging.
    popd >nul
    exit /b 1
)
git diff --cached --quiet --ignore-submodules -- . ":(exclude)%GENERATED_REFERENCE_CACHE%"
if errorlevel 1 (
    echo ERROR: Staged files differ from commit %SOURCE_COMMIT%. Commit the intended source before packaging.
    popd >nul
    exit /b 1
)
set "UNTRACKED_SOURCE="
for /f "delims=" %%I in ('git ls-files --others --exclude-standard') do if not defined UNTRACKED_SOURCE set "UNTRACKED_SOURCE=%%I"
if defined UNTRACKED_SOURCE (
    echo ERROR: Untracked source exists at "%UNTRACKED_SOURCE%". Commit or remove it before packaging.
    popd >nul
    exit /b 1
)
echo Source commit: %SOURCE_COMMIT%

echo [2/6] Ensuring an exact Release application build...
call :validate_candidate_build
set "CANDIDATE_BUILD_EXIT=!ERRORLEVEL!"
if "!CANDIDATE_BUILD_EXIT!"=="0" (
    echo Reusing the existing commit-exact Release application.
) else (
    call "%ROOT%build.bat" /s
    set "BUILD_EXIT=!ERRORLEVEL!"
    if not "!BUILD_EXIT!"=="0" (
        echo ERROR: The Release application build failed with exit code !BUILD_EXIT!; no installer was created.
        popd >nul
        exit /b !BUILD_EXIT!
    )
    call :validate_candidate_build
    set "CANDIDATE_BUILD_EXIT=!ERRORLEVEL!"
    if not "!CANDIDATE_BUILD_EXIT!"=="0" (
        echo ERROR: The Release build did not produce commit-exact executable provenance.
        popd >nul
        exit /b !CANDIDATE_BUILD_EXIT!
    )
)

echo [3/6] Locating Inno Setup 6...
call :find_iscc
if not defined ISCC (
    echo Inno Setup 6 was not found. Installing canonical winget package JRSoftware.InnoSetup...
    where winget.exe >nul 2>&1 || (
        echo ERROR: Missing dependency: Inno Setup 6.
        echo Tried installed locations and winget, but winget is unavailable.
        popd >nul
        exit /b 1
    )
    winget install --id JRSoftware.InnoSetup --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity
    set "INNO_INSTALL_EXIT=!ERRORLEVEL!"
    if not "!INNO_INSTALL_EXIT!"=="0" (
        echo ERROR: Inno Setup installation failed through canonical winget package JRSoftware.InnoSetup.
        popd >nul
        exit /b !INNO_INSTALL_EXIT!
    )
    call :find_iscc
)
if not defined ISCC (
    echo ERROR: Inno Setup installed without a discoverable ISCC.exe.
    popd >nul
    exit /b 1
)
echo Found Inno Setup compiler: "%ISCC%"

echo [4/6] Packaging an unsigned installer...
"%ISCC%" "/DSourceCommit=%SOURCE_COMMIT%" "%SCRIPT%"
set "ISCC_EXIT=!ERRORLEVEL!"
if not "!ISCC_EXIT!"=="0" (
    echo ERROR: Inno Setup failed to package "%SCRIPT%".
    popd >nul
    exit /b !ISCC_EXIT!
)

set "INSTALLER=%INSTALLER_DIR%\WindowsServerTools-Setup-%SOURCE_COMMIT%.exe"
echo [5/6] Verifying installer shape and unsigned status...
if not exist "%INSTALLER%" (
    echo ERROR: Inno Setup returned success but the installer is missing: "%INSTALLER%".
    popd >nul
    exit /b 1
)
for %%I in ("%INSTALLER%") do set "INSTALLER_SIZE=%%~zI"
if %INSTALLER_SIZE% LSS 102400 (
    echo ERROR: Installer is unexpectedly small: %INSTALLER_SIZE% bytes at "%INSTALLER%".
    popd >nul
    exit /b 1
)
for /f "usebackq delims=" %%S in (`powershell.exe -NoProfile -Command "(Get-AuthenticodeSignature -LiteralPath '%INSTALLER%').Status"`) do set "SIGNATURE_STATUS=%%S"
if /i not "%SIGNATURE_STATUS%"=="NotSigned" (
    echo ERROR: Expected an unsigned installer, but signature status is "%SIGNATURE_STATUS%".
    popd >nul
    exit /b 1
)

echo [6/6] Calculating SHA-256...
set "WST_HASH_TARGET=%INSTALLER%"
for /f "usebackq delims=" %%H in (`powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$stream=[IO.File]::OpenRead($env:WST_HASH_TARGET); try { $sha=[Security.Cryptography.SHA256]::Create(); try { ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() } } finally { $stream.Dispose() }"`) do set "INSTALLER_SHA256=%%H"
if not defined INSTALLER_SHA256 (
    echo ERROR: Could not calculate the installer SHA-256.
    popd >nul
    exit /b 1
)

echo Installer build complete.
echo Source commit: %SOURCE_COMMIT%
echo Installer: "%INSTALLER%"
echo Size: %INSTALLER_SIZE% bytes
echo SHA-256: %INSTALLER_SHA256%
echo Signature status: UNSIGNED ^(NotSigned^)
echo This project intentionally does not code-sign release artifacts.

popd >nul
exit /b 0

:find_iscc
set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
exit /b 0

:validate_candidate_build
if not exist "%PROJECT_EXE%" exit /b 1
if not exist "%COMMIT_FILE%" exit /b 1
if not exist "%BUILD_HASH_FILE%" exit /b 1
set "BUILT_COMMIT="
set "RECORDED_BUILD_HASH="
set "CURRENT_BUILD_HASH="
set /p "BUILT_COMMIT=" < "%COMMIT_FILE%"
set /p "RECORDED_BUILD_HASH=" < "%BUILD_HASH_FILE%"
if /i not "%BUILT_COMMIT%"=="%SOURCE_COMMIT%" exit /b 1
set "WST_HASH_TARGET=%PROJECT_EXE%"
for /f "usebackq delims=" %%H in (`powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$stream=[IO.File]::OpenRead($env:WST_HASH_TARGET); try { $sha=[Security.Cryptography.SHA256]::Create(); try { ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() } } finally { $stream.Dispose() }"`) do set "CURRENT_BUILD_HASH=%%H"
if not defined CURRENT_BUILD_HASH exit /b 1
if /i not "%CURRENT_BUILD_HASH%"=="%RECORDED_BUILD_HASH%" exit /b 1
exit /b 0

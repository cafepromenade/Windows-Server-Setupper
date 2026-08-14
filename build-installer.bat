@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "PROJECT_OUTPUT=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Release"
set "PROJECT_EXE=%PROJECT_OUTPUT%\Windows-Server-Tools.exe"
set "COMMIT_FILE=%PROJECT_OUTPUT%\source-commit.txt"
set "BUILD_HASH_FILE=%PROJECT_OUTPUT%\source-executable.sha256"
set "SCRIPT=%ROOT%packaging\WindowsServerTools.iss"
set "INSTALLER_DIR=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Installer"
set "EXCHANGE_ROOT=%ROOT%Windows-Server-Tools\Exchange-Auto-Installer"
set "EXCHANGE_DIST=%ROOT%Windows-Server-Tools\Exchange-Auto-Installer\dist"
set "WPF_INSTALLER_VERSION_FILE=%INSTALLER_DIR%\package-version.txt"
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

echo [1/10] Recording the exact source commit...
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
git diff --cached --quiet --ignore-submodules --
if errorlevel 1 (
    echo ERROR: Staged files differ from commit %SOURCE_COMMIT%. Commit the intended source before packaging.
    popd >nul
    exit /b 1
)
git diff --quiet --ignore-submodules --
if errorlevel 1 (
    echo ERROR: Tracked files differ from commit %SOURCE_COMMIT%. This script never restores or discards local edits.
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

set "PACKAGE_VERSION=%WST_RELEASE_VERSION%"
if not defined PACKAGE_VERSION (
    for /f "usebackq delims=" %%I in (`powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$ErrorActionPreference='Stop'; $packageJson=Get-Content -LiteralPath (Join-Path $env:EXCHANGE_ROOT 'package.json') -Raw; (ConvertFrom-Json -InputObject $packageJson).version"`) do set "PACKAGE_VERSION=%%I"
)
echo %PACKAGE_VERSION%| findstr /r /x "[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*" >nul || (
    echo ERROR: Shared installer version must be numeric major.minor.patch text; got "%PACKAGE_VERSION%".
    popd >nul
    exit /b 1
)
echo Shared installer version: %PACKAGE_VERSION%

echo [2/10] Ensuring exact runnable application builds...
call :validate_candidate_build
set "CANDIDATE_BUILD_EXIT=!ERRORLEVEL!"
if "!CANDIDATE_BUILD_EXIT!"=="0" (
    echo Reusing the existing commit-exact Release application.
) else (
    call "%ROOT%build.bat" /s
    set "BUILD_EXIT=!ERRORLEVEL!"
    if not "!BUILD_EXIT!"=="0" (
        echo ERROR: The Release application build failed with exit code !BUILD_EXIT!; no installer was created.
        set "SCRIPT_EXIT=!BUILD_EXIT!"
        goto :return_nested_build_failure
    )
    call :validate_candidate_build
    set "CANDIDATE_BUILD_EXIT=!ERRORLEVEL!"
    if not "!CANDIDATE_BUILD_EXIT!"=="0" (
        echo ERROR: The Release build did not produce commit-exact executable provenance.
        set "SCRIPT_EXIT=!CANDIDATE_BUILD_EXIT!"
        goto :return_nested_build_failure
    )
)

echo [3/10] Locating Inno Setup 6...
call :find_iscc
if not defined ISCC (
    echo ERROR: Missing dependency: Inno Setup 6.7.3-compatible compiler.
    echo Tried compatible installed copies, pinned winget, and the SHA-256-verified official GitHub release installer.
    popd >nul
    exit /b 1
)
echo Found Inno Setup compiler: "%ISCC%"

echo [4/10] Packaging the unsigned primary WPF installer...
"%ISCC%" "/DSourceCommit=%SOURCE_COMMIT%" "/DReleaseVersion=%PACKAGE_VERSION%" "%SCRIPT%"
set "ISCC_EXIT=!ERRORLEVEL!"
if not "!ISCC_EXIT!"=="0" (
    echo ERROR: Inno Setup failed to package "%SCRIPT%".
    popd >nul
    exit /b !ISCC_EXIT!
)

set "INSTALLER=%INSTALLER_DIR%\WindowsServerTools-Setup-%SOURCE_COMMIT%.exe"
echo [5/10] Verifying the primary installer shape and unsigned status...
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
set "WST_SIGNATURE_TARGET=%INSTALLER%"
powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$ErrorActionPreference='Stop'; $stream=[IO.File]::Open($env:WST_SIGNATURE_TARGET,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read); $reader=New-Object IO.BinaryReader($stream); try { if ($stream.Length -lt 64) { throw 'The installer is too short to be a valid PE file.'; } if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'The installer does not have an MZ header.'; } $stream.Position=0x3C; $peOffset=$reader.ReadUInt32(); if ($peOffset -gt ($stream.Length-24)) { throw 'The PE header offset is outside the installer.'; } $stream.Position=$peOffset; if ($reader.ReadUInt32() -ne 0x00004550) { throw 'The installer does not have a PE signature.'; } [void]$reader.ReadUInt16(); [void]$reader.ReadUInt16(); [void]$reader.ReadUInt32(); [void]$reader.ReadUInt32(); [void]$reader.ReadUInt32(); $optionalSize=$reader.ReadUInt16(); [void]$reader.ReadUInt16(); $optionalStart=$stream.Position; if ($optionalSize -lt 2 -or ($optionalStart+$optionalSize) -gt $stream.Length) { throw 'The PE optional header is invalid.'; } $magic=$reader.ReadUInt16(); if ($magic -eq 0x10B) { $directoryCountOffset=92; $directoryTableOffset=96; } elseif ($magic -eq 0x20B) { $directoryCountOffset=108; $directoryTableOffset=112; } else { throw 'The PE optional-header format is unsupported.'; } if ($optionalSize -lt ($directoryTableOffset+40)) { throw 'The PE optional header does not contain a complete security-directory entry.'; } $stream.Position=$optionalStart+$directoryCountOffset; $directoryCount=$reader.ReadUInt32(); if ($directoryCount -le 4) { 'NotSigned'; exit 0; } $stream.Position=$optionalStart+$directoryTableOffset+32; $certificateOffset=$reader.ReadUInt32(); $certificateSize=$reader.ReadUInt32(); if (($certificateOffset -eq 0) -and ($certificateSize -eq 0)) { 'NotSigned'; exit 0; } if (($certificateOffset -eq 0) -or ($certificateSize -eq 0)) { throw 'The PE security-directory entry is inconsistent.'; } if (($certificateOffset+[uint64]$certificateSize) -gt [uint64]$stream.Length) { throw 'The PE certificate table extends beyond the installer.'; } throw ('The installer contains a PE certificate table at offset '+$certificateOffset+' with size '+$certificateSize+'.'); } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1; } finally { $reader.Dispose(); $stream.Dispose(); }"
set "SIGNATURE_CHECK_EXIT=!ERRORLEVEL!"
if not "!SIGNATURE_CHECK_EXIT!"=="0" (
    echo ERROR: The installer is signed or its PE certificate table could not be validated safely.
    popd >nul
    exit /b !SIGNATURE_CHECK_EXIT!
)
powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$ErrorActionPreference='Stop'; $expected=[Version]$env:PACKAGE_VERSION; $actual=[Version](Get-Item -LiteralPath $env:INSTALLER).VersionInfo.FileVersion; if ($actual.Major -ne $expected.Major -or $actual.Minor -ne $expected.Minor -or $actual.Build -ne $expected.Build) { throw ('WPF installer version '+$actual+' does not match shared package version '+$expected+'.') }"
set "WPF_VERSION_EXIT=!ERRORLEVEL!"
if not "!WPF_VERSION_EXIT!"=="0" (
    echo ERROR: The primary installer does not carry shared version %PACKAGE_VERSION%.
    popd >nul
    exit /b !WPF_VERSION_EXIT!
)
>"%WPF_INSTALLER_VERSION_FILE%" echo %PACKAGE_VERSION%

echo [6/10] Calculating the primary installer SHA-256...
set "WST_HASH_TARGET=%INSTALLER%"
for /f "usebackq delims=" %%H in (`powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$stream=[IO.File]::OpenRead($env:WST_HASH_TARGET); try { $sha=[Security.Cryptography.SHA256]::Create(); try { ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() } } finally { $stream.Dispose() }"`) do set "INSTALLER_SHA256=%%H"
if not defined INSTALLER_SHA256 (
    echo ERROR: Could not calculate the installer SHA-256.
    popd >nul
    exit /b 1
)

echo [7/10] Locating the pinned Node.js toolchain...
call :find_node
if not defined NODE_HOME (
    echo ERROR: Node.js bootstrap did not return a usable toolchain directory.
    popd >nul
    exit /b 1
)
set "PATH=%NODE_HOME%;%PATH%"
if not exist "%NODE_HOME%\node.exe" (
    echo ERROR: Missing dependency: node.exe was not found in "%NODE_HOME%".
    popd >nul
    exit /b 1
)
if not exist "%NODE_HOME%\npm.cmd" (
    echo ERROR: Missing dependency: npm.cmd was not found in "%NODE_HOME%".
    popd >nul
    exit /b 1
)

echo [8/10] Packaging the Exchange Auto Installer through isolated Squirrel.Windows staging...
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%ROOT%scripts\package-exchange.ps1" -NodeHome "%NODE_HOME%" -SourceCommit "%SOURCE_COMMIT%" -Version "%PACKAGE_VERSION%"
set "EXCHANGE_PACKAGE_EXIT=!ERRORLEVEL!"
if not "!EXCHANGE_PACKAGE_EXIT!"=="0" (
    echo ERROR: Exchange Auto Installer packaging failed with exit code !EXCHANGE_PACKAGE_EXIT!.
    popd >nul
    exit /b !EXCHANGE_PACKAGE_EXIT!
)

echo [9/10] Verifying Squirrel.Windows setup, RELEASES, full package, provenance, and unsigned state...
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%ROOT%scripts\verify-exchange-package.ps1" -SourceCommit "%SOURCE_COMMIT%" -ExpectedVersion "%PACKAGE_VERSION%"
set "EXCHANGE_VERIFY_EXIT=!ERRORLEVEL!"
if not "!EXCHANGE_VERIFY_EXIT!"=="0" (
    echo ERROR: Exchange Squirrel.Windows output verification failed with exit code !EXCHANGE_VERIFY_EXIT!.
    popd >nul
    exit /b !EXCHANGE_VERIFY_EXIT!
)

echo [10/10] Confirming the checkout still matches the source commit...
git diff --quiet --ignore-submodules --
if errorlevel 1 (
    echo ERROR: Packaging changed tracked files after commit %SOURCE_COMMIT%.
    popd >nul
    exit /b 1
)
git diff --cached --quiet --ignore-submodules --
if errorlevel 1 (
    echo ERROR: Packaging staged tracked changes after commit %SOURCE_COMMIT%.
    popd >nul
    exit /b 1
)

echo Installer build complete.
echo Source commit: %SOURCE_COMMIT%
echo Shared installer version: %PACKAGE_VERSION%
echo Primary installer: "%INSTALLER%"
echo Primary installer size: %INSTALLER_SIZE% bytes
echo Primary installer SHA-256: %INSTALLER_SHA256%
echo Exchange Squirrel.Windows output: "%EXCHANGE_DIST%\squirrel-windows"
echo Signature status: UNSIGNED ^(both setup executables and the unpacked Exchange application have no PE certificate table^)
echo This project intentionally does not code-sign any release artifact.

popd >nul
exit /b 0

:return_nested_build_failure
popd >nul
endlocal & exit /b %SCRIPT_EXIT%

:find_iscc
set "ISCC="
for /f "usebackq delims=" %%I in (`powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%ROOT%scripts\ensure-inno.ps1"`) do set "ISCC=%%I"
exit /b 0

:find_node
set "NODE_HOME="
for /f "usebackq delims=" %%I in (`powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%ROOT%scripts\ensure-node.ps1"`) do set "NODE_HOME=%%I"
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

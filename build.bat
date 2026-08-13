@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "PROJECT=%ROOT%Windows-Server-Tools\Windows-Server-Tools\Windows-Server-Tools.csproj"
set "OUTPUT=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Release\Windows-Server-Tools.exe"
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

echo [1/4] Locating Microsoft Build Tools and the .NET Framework 4.7.2 toolchain...
call :find_msbuild
if not defined MSBUILD (
    echo Microsoft Build Tools were not found. Installing the canonical Microsoft package with winget...
    where winget.exe >nul 2>&1 || (
        echo ERROR: Missing dependency: Microsoft Build Tools with the .NET Framework 4.7.2 targeting pack.
        echo Tried installed Visual Studio instances and winget, but winget is unavailable.
        popd >nul
        exit /b 1
    )
    winget install --id Microsoft.VisualStudio.2022.BuildTools --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity --override "--wait --passive --norestart --add Microsoft.VisualStudio.Workload.MSBuildTools --add Microsoft.Net.Component.4.7.2.TargetingPack --add Microsoft.Net.Component.4.7.2.SDK --includeRecommended"
    if errorlevel 1 (
        echo ERROR: Microsoft Build Tools installation failed through canonical winget package Microsoft.VisualStudio.2022.BuildTools.
        popd >nul
        exit /b 1
    )
    call :find_msbuild
)

if not defined MSBUILD (
    echo ERROR: Microsoft Build Tools installed without a discoverable MSBuild.exe.
    echo Required components: MSBuild and the .NET Framework 4.7.2 targeting pack.
    popd >nul
    exit /b 1
)
echo Found MSBuild: "%MSBUILD%"

echo [2/4] Restoring declared NuGet packages...
"%MSBUILD%" "%PROJECT%" /t:Restore /m /nologo /verbosity:minimal /p:RestorePackagesConfig=true /p:Configuration=Release /p:Platform="Any CPU"
if errorlevel 1 (
    echo ERROR: Package restore failed for "%PROJECT%".
    popd >nul
    exit /b 1
)

echo [3/4] Building the Release application...
"%MSBUILD%" "%PROJECT%" /t:Build /m /nologo /verbosity:minimal /p:Configuration=Release /p:Platform="Any CPU"
if errorlevel 1 (
    echo ERROR: Release build failed for "%PROJECT%".
    popd >nul
    exit /b 1
)

echo [4/4] Verifying the runnable application...
if not exist "%OUTPUT%" (
    echo ERROR: MSBuild returned success but the application is missing: "%OUTPUT%".
    popd >nul
    exit /b 1
)

for %%I in ("%OUTPUT%") do set "OUTPUT_SIZE=%%~zI"
if "%OUTPUT_SIZE%"=="0" (
    echo ERROR: The built application is empty: "%OUTPUT%".
    popd >nul
    exit /b 1
)

echo Build complete.
echo Application: "%OUTPUT%"
echo Size: %OUTPUT_SIZE% bytes

if "%SILENT_MODE%"=="1" (
    popd >nul
    exit /b 0
)

set "RUN_NOW="
set /p "RUN_NOW=Run the application now? [y/N] "
if /i "%RUN_NOW%"=="Y" start "" "%OUTPUT%"

popd >nul
exit /b 0

:find_msbuild
set "MSBUILD="
set "VSWHERE="
if exist "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not defined VSWHERE if exist "%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe" set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
if defined VSWHERE (
    for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe 2^>nul`) do if not defined MSBUILD set "MSBUILD=%%I"
)
if not defined MSBUILD (
    for /f "delims=" %%I in ('where MSBuild.exe 2^>nul') do if not defined MSBUILD set "MSBUILD=%%I"
)
exit /b 0

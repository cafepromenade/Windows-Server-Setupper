@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%Windows-Server-Tools\Windows-Server-Tools.sln"
set "PROJECT=%ROOT%Windows-Server-Tools\Windows-Server-Tools\Windows-Server-Tools.csproj"
set "OUTPUT=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Release\Windows-Server-Tools.exe"
set "SOLUTION_PLATFORM=Any CPU"
set "PROJECT_PLATFORM=AnyCPU"
set "WST_REFERENCE_VERSION=1.0.3"
set "WST_REFERENCE_CACHE=%LOCALAPPDATA%\WindowsServerTools\toolchain\microsoft.netframework.referenceassemblies.net472.%WST_REFERENCE_VERSION%"
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

echo [1/5] Locating Microsoft Build Tools...
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

echo [2/5] Locating .NET Framework 4.7.2 reference assemblies...
call :find_framework_path
if not defined FRAMEWORK_PATH (
    if not defined LOCALAPPDATA (
        echo ERROR: LOCALAPPDATA is unavailable, so a user-owned reference-assembly cache cannot be created.
        popd >nul
        exit /b 1
    )
    echo The targeting pack is absent. Downloading the official Microsoft reference-assembly package from NuGet.org...
    call :install_reference_assemblies
    if errorlevel 1 (
        echo ERROR: Could not install Microsoft.NETFramework.ReferenceAssemblies.net472 %WST_REFERENCE_VERSION% in the user-owned cache.
        popd >nul
        exit /b 1
    )
    call :find_framework_path
)
if not defined FRAMEWORK_PATH (
    echo ERROR: The expected build\.NETFramework\v4.7.2 reference-assembly path is absent after bootstrap.
    echo Cache checked: "%WST_REFERENCE_CACHE%".
    popd >nul
    exit /b 1
)
echo Reference assemblies: "%FRAMEWORK_PATH%"

echo [3/5] Restoring declared NuGet packages...
"%MSBUILD%" "%SOLUTION%" /t:Restore /m /nologo /verbosity:minimal /p:RestorePackagesConfig=true /p:Configuration=Release /p:Platform="%SOLUTION_PLATFORM%" /p:FrameworkPathOverride="%FRAMEWORK_PATH%"
if errorlevel 1 (
    echo ERROR: Package restore failed for "%SOLUTION%".
    popd >nul
    exit /b 1
)

echo [4/5] Building the Release application...
"%MSBUILD%" "%PROJECT%" /t:Build /m /nologo /verbosity:minimal /p:Configuration=Release /p:Platform="%PROJECT_PLATFORM%" /p:FrameworkPathOverride="%FRAMEWORK_PATH%"
if errorlevel 1 (
    echo ERROR: Release build failed for "%PROJECT%".
    popd >nul
    exit /b 1
)

echo [5/5] Verifying the runnable application...
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

:find_framework_path
set "FRAMEWORK_PATH="
if exist "%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\mscorlib.dll" if exist "%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.dll" set "FRAMEWORK_PATH=%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2"
if not defined FRAMEWORK_PATH if exist "%ProgramFiles%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\mscorlib.dll" if exist "%ProgramFiles%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2\System.dll" set "FRAMEWORK_PATH=%ProgramFiles%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2"
if not defined FRAMEWORK_PATH if exist "%WST_REFERENCE_CACHE%\build\.NETFramework\v4.7.2\mscorlib.dll" if exist "%WST_REFERENCE_CACHE%\build\.NETFramework\v4.7.2\System.dll" set "FRAMEWORK_PATH=%WST_REFERENCE_CACHE%\build\.NETFramework\v4.7.2"
exit /b 0

:install_reference_assemblies
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $id='microsoft.netframework.referenceassemblies.net472'; $version=$env:WST_REFERENCE_VERSION; $target=[IO.Path]::GetFullPath($env:WST_REFERENCE_CACHE); $base='https://api.nuget.org/v3-flatcontainer/'+$id+'/'+$version+'/'; $packageName=$id+'.'+$version+'.nupkg'; $scratch=Join-Path ([IO.Path]::GetTempPath()) ('wst-net472-'+[Guid]::NewGuid().ToString('N')); $archive=Join-Path $scratch 'reference-assemblies.zip'; $hashFile=Join-Path $scratch 'reference-assemblies.sha512'; $stage=Join-Path $scratch 'expanded'; New-Item -ItemType Directory -Path $stage -Force | Out-Null; try { Invoke-WebRequest -UseBasicParsing -Uri ($base+$packageName) -OutFile $archive; Invoke-WebRequest -UseBasicParsing -Uri ($base+$packageName+'.sha512') -OutFile $hashFile; $expected=(Get-Content -LiteralPath $hashFile -Raw).Trim(); $stream=[IO.File]::OpenRead($archive); try { $actual=[Convert]::ToBase64String(([Security.Cryptography.SHA512]::Create()).ComputeHash($stream)); } finally { $stream.Dispose(); } if ($actual -cne $expected) { throw 'NuGet package SHA-512 verification failed.'; } Expand-Archive -LiteralPath $archive -DestinationPath $stage -Force; $framework=Join-Path $stage 'build\.NETFramework\v4.7.2'; if (-not (Test-Path -LiteralPath (Join-Path $framework 'mscorlib.dll') -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $framework 'System.dll') -PathType Leaf)) { throw 'The package does not contain the expected build/.NETFramework/v4.7.2 reference assemblies.'; } $parent=Split-Path -Parent $target; New-Item -ItemType Directory -Path $parent -Force | Out-Null; if (Test-Path -LiteralPath $target) { $preserved=$target+'.invalid-'+[Guid]::NewGuid().ToString('N'); Move-Item -LiteralPath $target -Destination $preserved; } Move-Item -LiteralPath $stage -Destination $target; } finally { if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue; } }"
exit /b %ERRORLEVEL%

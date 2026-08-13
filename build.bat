@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%Windows-Server-Tools\Windows-Server-Tools.sln"
set "PROJECT=%ROOT%Windows-Server-Tools\Windows-Server-Tools\Windows-Server-Tools.csproj"
set "OUTPUT=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Release\Windows-Server-Tools.exe"
set "BUILD_COMMIT_FILE=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Release\source-commit.txt"
set "BUILD_HASH_FILE=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Release\source-executable.sha256"
set "GENERATED_REFERENCE_CACHE=Windows-Server-Tools/Windows-Server-Tools/obj/Release/Windows-Server-Tools.csproj.AssemblyReference.cache"
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
call :capture_source_identity

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
    set "REFERENCE_INSTALL_EXIT=!ERRORLEVEL!"
    if not "!REFERENCE_INSTALL_EXIT!"=="0" (
        echo ERROR: Could not install Microsoft.NETFramework.ReferenceAssemblies.net472 %WST_REFERENCE_VERSION% in the user-owned cache.
        popd >nul
        exit /b !REFERENCE_INSTALL_EXIT!
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

call :write_build_provenance
set "PROVENANCE_EXIT=%ERRORLEVEL%"
if not "%PROVENANCE_EXIT%"=="0" (
    echo ERROR: The application was built, but exact source provenance could not be recorded.
    popd >nul
    exit /b %PROVENANCE_EXIT%
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
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $id='microsoft.netframework.referenceassemblies.net472'; $version=$env:WST_REFERENCE_VERSION; $target=[IO.Path]::GetFullPath($env:WST_REFERENCE_CACHE); $flatRoot='https://api.nuget.org/v3-flatcontainer/'; $indexUri=$flatRoot+$id+'/index.json'; $packageUri=$flatRoot+$id+'/'+$version+'/'+$id+'.'+$version+'.nupkg'; $registrationUri='https://api.nuget.org/v3/registration5-semver1/'+$id+'/'+$version+'.json'; $index=Invoke-RestMethod -UseBasicParsing -Uri $indexUri; if (-not ($index.versions -ccontains $version)) { throw ('Pinned version '+$version+' is absent from '+$indexUri); } $registration=Invoke-RestMethod -UseBasicParsing -Uri $registrationUri; if ($registration.packageContent -cne $packageUri) { throw 'NuGet registration metadata did not resolve to the exact lowercase v3 flat-container package URL.'; } $catalog=Invoke-RestMethod -UseBasicParsing -Uri $registration.catalogEntry; if (-not $registration.listed -or -not $catalog.listed -or $catalog.id -ine 'Microsoft.NETFramework.ReferenceAssemblies.net472' -or $catalog.version -cne $version -or $catalog.packageHashAlgorithm -cne 'SHA512' -or [string]::IsNullOrWhiteSpace($catalog.packageHash)) { throw 'NuGet registration or catalog metadata failed validation.'; } $scratch=Join-Path ([IO.Path]::GetTempPath()) ('wst-net472-'+[Guid]::NewGuid().ToString('N')); $archive=Join-Path $scratch 'reference-assemblies.zip'; $stage=Join-Path $scratch 'expanded'; New-Item -ItemType Directory -Path $stage -Force | Out-Null; try { Invoke-WebRequest -UseBasicParsing -Uri $packageUri -OutFile $archive; $stream=[IO.File]::OpenRead($archive); try { $actual=[Convert]::ToBase64String(([Security.Cryptography.SHA512]::Create()).ComputeHash($stream)); } finally { $stream.Dispose(); } if ($actual -cne $catalog.packageHash) { throw 'NuGet package SHA-512 verification failed.'; } Expand-Archive -LiteralPath $archive -DestinationPath $stage -Force; $framework=Join-Path $stage 'build\.NETFramework\v4.7.2'; if (-not (Test-Path -LiteralPath (Join-Path $framework 'mscorlib.dll') -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $framework 'System.dll') -PathType Leaf)) { throw 'The package does not contain the expected build/.NETFramework/v4.7.2 reference assemblies.'; } $parent=Split-Path -Parent $target; New-Item -ItemType Directory -Path $parent -Force | Out-Null; if (Test-Path -LiteralPath $target) { $preserved=$target+'.invalid-'+[Guid]::NewGuid().ToString('N'); Move-Item -LiteralPath $target -Destination $preserved; } Move-Item -LiteralPath $stage -Destination $target; } finally { if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue; } }"
set "REFERENCE_DOWNLOAD_EXIT=!ERRORLEVEL!"
exit /b !REFERENCE_DOWNLOAD_EXIT!

:capture_source_identity
set "SOURCE_COMMIT="
set "SOURCE_IS_CLEAN=0"
where git.exe >nul 2>&1 || exit /b 0
git diff --quiet --ignore-submodules -- . ":(exclude)%GENERATED_REFERENCE_CACHE%" || exit /b 0
git diff --cached --quiet --ignore-submodules -- . ":(exclude)%GENERATED_REFERENCE_CACHE%" || exit /b 0
set "UNTRACKED_SOURCE="
for /f "delims=" %%I in ('git ls-files --others --exclude-standard') do if not defined UNTRACKED_SOURCE set "UNTRACKED_SOURCE=%%I"
if defined UNTRACKED_SOURCE exit /b 0
for /f "delims=" %%I in ('git rev-parse HEAD 2^>nul') do set "SOURCE_COMMIT=%%I"
if defined SOURCE_COMMIT set "SOURCE_IS_CLEAN=1"
exit /b 0

:write_build_provenance
if not "%SOURCE_IS_CLEAN%"=="1" (
    if exist "%BUILD_COMMIT_FILE%" del /q "%BUILD_COMMIT_FILE%" >nul 2>&1
    if exist "%BUILD_HASH_FILE%" del /q "%BUILD_HASH_FILE%" >nul 2>&1
    echo Build provenance was not recorded because the source checkout was not commit-exact before the build.
    exit /b 0
)
set "BUILD_SHA256="
set "WST_HASH_TARGET=%OUTPUT%"
for /f "usebackq delims=" %%H in (`powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$stream=[IO.File]::OpenRead($env:WST_HASH_TARGET); try { $sha=[Security.Cryptography.SHA256]::Create(); try { ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() } } finally { $stream.Dispose() }"`) do set "BUILD_SHA256=%%H"
if not defined BUILD_SHA256 exit /b 1
>"%BUILD_COMMIT_FILE%" echo %SOURCE_COMMIT%
>"%BUILD_HASH_FILE%" echo %BUILD_SHA256%
echo Build source commit: %SOURCE_COMMIT%
echo Application SHA-256: %BUILD_SHA256%
exit /b 0

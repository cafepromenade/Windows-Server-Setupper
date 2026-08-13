@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "SOLUTION=%ROOT%Windows-Server-Tools\Windows-Server-Tools.sln"
set "PROJECT=%ROOT%Windows-Server-Tools\Windows-Server-Tools\Windows-Server-Tools.csproj"
set "PROJECT_OUTPUT=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Release"
set "OUTPUT=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Release\Windows-Server-Tools.exe"
set "BUILD_COMMIT_FILE=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Release\source-commit.txt"
set "BUILD_HASH_FILE=%ROOT%Windows-Server-Tools\Windows-Server-Tools\bin\Release\source-executable.sha256"
set "EXCHANGE_ROOT=%ROOT%Windows-Server-Tools\Exchange-Auto-Installer"
set "EXCHANGE_OUTPUT=%ROOT%Windows-Server-Tools\Exchange-Auto-Installer\dist\win-unpacked\Exchange Auto Installer.exe"
set "EXCHANGE_ASAR=%ROOT%Windows-Server-Tools\Exchange-Auto-Installer\dist\win-unpacked\resources\app.asar"
set "EXCHANGE_COMMIT_FILE=%ROOT%Windows-Server-Tools\Exchange-Auto-Installer\dist\source-commit.txt"
set "EXCHANGE_HASH_FILE=%ROOT%Windows-Server-Tools\Exchange-Auto-Installer\dist\source-executable.sha256"
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
if not "%SOURCE_IS_CLEAN%"=="1" (
    echo ERROR: The tracked and untracked source checkout must match one exact commit before building.
    echo Ignored dependency and generated-output directories do not affect this check.
    popd >nul
    exit /b 1
)

echo [1/9] Verifying committed multi-resolution application and installer icons...
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%ROOT%scripts\verify-application-icons.ps1"
if errorlevel 1 (
    echo ERROR: Application/installer icon verification failed.
    popd >nul
    exit /b 1
)

call :clean_primary_output
set "CLEAN_OUTPUT_EXIT=%ERRORLEVEL%"
if not "%CLEAN_OUTPUT_EXIT%"=="0" (
    echo ERROR: The validated primary Release output directory could not be cleared.
    popd >nul
    exit /b %CLEAN_OUTPUT_EXIT%
)

echo [2/9] Locating Microsoft Build Tools...
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

echo [3/9] Locating .NET Framework 4.7.2 reference assemblies...
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

echo [4/9] Restoring declared NuGet packages...
"%MSBUILD%" "%SOLUTION%" /t:Restore /m /nologo /verbosity:minimal /p:RestorePackagesConfig=true /p:Configuration=Release /p:Platform="%SOLUTION_PLATFORM%" /p:FrameworkPathOverride="%FRAMEWORK_PATH%"
if errorlevel 1 (
    echo ERROR: Package restore failed for "%SOLUTION%".
    popd >nul
    exit /b 1
)

echo [5/9] Building the primary WPF Release application...
"%MSBUILD%" "%PROJECT%" /t:Build /m /nologo /verbosity:minimal /p:Configuration=Release /p:Platform="%PROJECT_PLATFORM%" /p:FrameworkPathOverride="%FRAMEWORK_PATH%"
if errorlevel 1 (
    echo ERROR: Release build failed for "%PROJECT%".
    popd >nul
    exit /b 1
)

echo [6/9] Verifying the primary runnable application...
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

call :restore_known_build_byproducts
set "GENERATED_RESTORE_EXIT=%ERRORLEVEL%"
if not "%GENERATED_RESTORE_EXIT%"=="0" (
    echo ERROR: Known tracked MSBuild byproducts could not be restored after the exact build.
    popd >nul
    exit /b %GENERATED_RESTORE_EXIT%
)
git diff --quiet --ignore-submodules --
if errorlevel 1 (
    echo ERROR: The WPF build changed tracked files outside the known generated byproduct list.
    popd >nul
    exit /b 1
)

echo [7/9] Locating the pinned Node.js toolchain...
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
for /f "delims=" %%I in ('"%NODE_HOME%\node.exe" --version') do set "NODE_VERSION=%%I"
echo Found Node.js: %NODE_VERSION% at "%NODE_HOME%"

echo [8/9] Restoring the Exchange Auto Installer lockfile dependencies...
pushd "%EXCHANGE_ROOT%" >nul || (
    echo ERROR: Could not enter the Exchange Auto Installer package directory: "%EXCHANGE_ROOT%".
    popd >nul
    popd >nul
    exit /b 1
)
call "%NODE_HOME%\npm.cmd" ci --no-audit --no-fund
set "NPM_CI_EXIT=!ERRORLEVEL!"
if not "!NPM_CI_EXIT!"=="0" (
    echo ERROR: npm ci failed for "%EXCHANGE_ROOT%" with exit code !NPM_CI_EXIT!.
    popd >nul
    popd >nul
    exit /b !NPM_CI_EXIT!
)

echo [9/9] Building and verifying the unpacked Exchange Auto Installer...
call "%NODE_HOME%\npm.cmd" run build
set "EXCHANGE_BUILD_EXIT=!ERRORLEVEL!"
popd >nul
if not "!EXCHANGE_BUILD_EXIT!"=="0" (
    echo ERROR: The Exchange Auto Installer build failed with exit code !EXCHANGE_BUILD_EXIT!.
    popd >nul
    exit /b !EXCHANGE_BUILD_EXIT!
)
if not exist "%EXCHANGE_OUTPUT%" (
    echo ERROR: Electron Builder returned success but the runnable application is missing: "%EXCHANGE_OUTPUT%".
    popd >nul
    exit /b 1
)
if not exist "%EXCHANGE_ASAR%" (
    echo ERROR: Electron Builder returned success but resources\app.asar is missing: "%EXCHANGE_ASAR%".
    popd >nul
    exit /b 1
)
for %%I in ("%EXCHANGE_OUTPUT%") do set "EXCHANGE_OUTPUT_SIZE=%%~zI"
if "!EXCHANGE_OUTPUT_SIZE!"=="0" (
    echo ERROR: The built Exchange Auto Installer executable is empty.
    popd >nul
    exit /b 1
)
call :write_exchange_provenance
set "EXCHANGE_PROVENANCE_EXIT=!ERRORLEVEL!"
if not "!EXCHANGE_PROVENANCE_EXIT!"=="0" (
    echo ERROR: Exchange Auto Installer provenance could not be recorded.
    popd >nul
    exit /b !EXCHANGE_PROVENANCE_EXIT!
)

echo Build complete.
echo Primary WPF application: "%OUTPUT%"
echo Primary WPF size: %OUTPUT_SIZE% bytes
echo Exchange Auto Installer: "%EXCHANGE_OUTPUT%"
echo Exchange Auto Installer size: !EXCHANGE_OUTPUT_SIZE! bytes

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

:find_node
set "NODE_HOME="
for /f "usebackq delims=" %%I in (`powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%ROOT%scripts\ensure-node.ps1"`) do set "NODE_HOME=%%I"
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

:clean_primary_output
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $root=[IO.Path]::GetFullPath($env:ROOT); $target=[IO.Path]::GetFullPath($env:PROJECT_OUTPUT); if (-not $target.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($target) -cne 'Release') { throw ('Refusing to clear unexpected build output: '+$target); }; if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force; }"
exit /b %ERRORLEVEL%

:restore_known_build_byproducts
git restore --source=HEAD -- "Windows-Server-Tools/Windows-Server-Tools/obj/Release/CommonlyInstalledWindowsComponents.g.cs" "Windows-Server-Tools/Windows-Server-Tools/obj/Release/MainWindow.g.cs" "Windows-Server-Tools/Windows-Server-Tools/obj/Release/Windows-Server-Tools.csproj.AssemblyReference.cache" "Windows-Server-Tools/Windows-Server-Tools/obj/Release/Windows-Server-Tools_MarkupCompile.cache" "Windows-Server-Tools/Windows-Server-Tools/obj/Release/Windows-Server-Tools_MarkupCompile.lref"
exit /b %ERRORLEVEL%

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

:write_exchange_provenance
set "EXCHANGE_SHA256="
set "WST_HASH_TARGET=%EXCHANGE_OUTPUT%"
for /f "usebackq delims=" %%H in (`powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$stream=[IO.File]::OpenRead($env:WST_HASH_TARGET); try { $sha=[Security.Cryptography.SHA256]::Create(); try { ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() } } finally { $stream.Dispose() }"`) do set "EXCHANGE_SHA256=%%H"
if not defined EXCHANGE_SHA256 exit /b 1
>"%EXCHANGE_COMMIT_FILE%" echo %SOURCE_COMMIT%
>"%EXCHANGE_HASH_FILE%" echo %EXCHANGE_SHA256%
echo Exchange build source commit: %SOURCE_COMMIT%
echo Exchange application SHA-256: %EXCHANGE_SHA256%
exit /b 0

# Windows Server Setupper

Windows Server Setupper contains Windows desktop tools for configuring server roles, baseline settings, directory services, shared folders, and selected software. The primary application is a .NET Framework 4.7.2 WPF project under `Windows-Server-Tools/Windows-Server-Tools`.

> [!WARNING]
> These tools change operating-system roles, network settings, security settings, scheduled tasks, and directory-service data. Build and evaluate them on an appropriate test server, review the requested operations, and run with administrative rights only when the operation requires them.

## Current status

The error-recovery hardening is being prepared for an expedited release. The current edits preserve durable, resumable recovery, truthful uncertain outcomes, protected state, and process-wide coordination that allows only one server-changing operation at a time across setup, directory, software, feature, web, and storage actions. They have not yet been represented here as an integrated default-branch commit, published installer, or tagged release.

Legacy Exchange/SCCM launch controls are reachable, but their call chains now propagate stopped results and fail closed when secure guided credential input is unavailable. They no longer proceed through the removed embedded-credential behavior.

The expedited release path intentionally does not run tests, review passes, or UI captures after the current edits. The results below are historical evidence from an earlier source state, not verification of the release candidate. There is no verified installer link for the current edits yet.

| Area | Current evidence |
| --- | --- |
| Focused recovery checks | Historical baseline: `PASS: 146 recovery checks`; not rerun for the current edits |
| Primary WPF compile | Historical matching-source scratch build succeeded; not a build of the current edits |
| Review and UI captures | Intentionally not run after the current edits under the expedited release path |
| Default-branch integration | Pending |
| Installer / tagged release | Pending; not claimed by this document |

## Documentation

- [Reliability documentation](./Windows-Server-Tools/docs/reliability/README.md)
- [Error recovery and resumable operations](./Windows-Server-Tools/docs/reliability/error-recovery.md)
- [Roadmap](./ROADMAP.md)
- [Current handoff](./HANDOFF.md)

## Build the primary application

The supported source project targets .NET Framework 4.7.2 and Visual Studio 2022. A normal development environment needs:

- Visual Studio 2022 with the **.NET desktop development** workload;
- the .NET Framework 4.7.2 developer/targeting pack;
- NuGet package restore enabled for the solution.

For a non-interactive Release build, run:

```powershell
.\build.bat /s
```

The script restores required packages and builds the primary Release executable. To build the unsigned installer from that executable, run:

```powershell
.\build-installer.bat /s
```

`build-installer.bat` calls `build.bat`, installs the canonical Inno Setup package through `winget` when it is missing, and uses `packaging/WindowsServerTools.iss`. It writes `WindowsServerTools-Setup-<commit>.exe` below `Windows-Server-Tools/Windows-Server-Tools/bin/Installer`, requires the result to report `NotSigned`, and prints its size, SHA-256 digest, and source commit. The script builds locally; publication and release verification are separate steps.

From a Developer PowerShell prompt:

```powershell
msbuild .\Windows-Server-Tools\Windows-Server-Tools.sln /t:Restore /p:RestorePackagesConfig=true
msbuild .\Windows-Server-Tools\Windows-Server-Tools\Windows-Server-Tools.csproj /t:Build /p:Configuration=Release /p:Platform="Any CPU"
```

The expected primary output is:

```text
Windows-Server-Tools\Windows-Server-Tools\bin\Release\Windows-Server-Tools.exe
```

The complete solution also contains legacy and supporting projects with their own runtime and packaging requirements. The current reliability evidence covers the primary WPF application and the focused recovery executable; it does not claim a complete release build of every solution project.

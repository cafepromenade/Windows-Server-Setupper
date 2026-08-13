# Windows Server Setupper

Windows Server Setupper contains Windows desktop tools for configuring server roles, baseline settings, directory services, shared folders, and selected software. The primary application is a .NET Framework 4.7.2 WPF project under `Windows-Server-Tools/Windows-Server-Tools`.

> [!WARNING]
> These tools change operating-system roles, network settings, security settings, scheduled tasks, and directory-service data. Build and evaluate them on an appropriate test server, review the requested operations, and run with administrative rights only when the operation requires them.

## Current status

The error-recovery hardening is implemented locally, but the source is still receiving final review changes and has not yet been represented here as an integrated commit, remote workflow result, installer, or release. There is no verified installer link for the current local changes. The results below are the latest completed local baseline; final verification must be rerun after the source is frozen.

| Area | Current evidence |
| --- | --- |
| Focused recovery checks | Latest completed baseline: `PASS: 146 recovery checks`; final rerun pending |
| Primary WPF compile | Earlier matching-source scratch build succeeded; final source-freeze rerun pending |
| Default-branch integration | Pending |
| Remote workflow / release | Not claimed |

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

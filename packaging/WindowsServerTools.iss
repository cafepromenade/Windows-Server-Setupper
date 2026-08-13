#ifndef SourceCommit
  #error SourceCommit must be supplied by build-installer.bat.
#endif

#define AppName "Windows Server Tools"
#define AppExecutable "Windows-Server-Tools.exe"
#define BuildOutput "..\Windows-Server-Tools\Windows-Server-Tools\bin\Release"
#define InstallerOutput "..\Windows-Server-Tools\Windows-Server-Tools\bin\Installer"

[Setup]
AppId={{67C5E166-98CD-4BC7-B4E9-1C5EF9A9605B}
AppName={#AppName}
AppVersion=0.1.0 ({#SourceCommit})
AppPublisher=cafepromenade
AppComments=Unsigned build from commit {#SourceCommit}
DefaultDirName={autopf}\Windows Server Tools
DefaultGroupName=Windows Server Tools
DisableProgramGroupPage=yes
OutputDir={#InstallerOutput}
OutputBaseFilename=WindowsServerTools-Setup-{#SourceCommit}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExecutable}
SignedUninstaller=no
SetupIconFile=..\Windows-Server-Tools\assets\branding\windows-server-setupper.ico

[Files]
Source: "{#BuildOutput}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\Windows Server Tools"; Filename: "{app}\{#AppExecutable}"; WorkingDir: "{app}"

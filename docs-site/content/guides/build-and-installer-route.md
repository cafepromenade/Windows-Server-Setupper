# Build and installer route
Category: Development
Suggested: product-overview,releases-changelog-and-downloads,resilient-recovery

## Application build

Run build.bat /s at the repository root to restore required packages and build the primary Release executable. The primary project targets .NET Framework 4.7.2 and the output is Windows-Server-Tools/Windows-Server-Tools/bin/Release/Windows-Server-Tools.exe.

## Installer build

Run build-installer.bat /s to call the application build and produce the unsigned installer through the repository's supported packaging path. The script verifies the output file, source commit, SHA-256 digest, and unsigned status before reporting success.

## Publication separation

The local scripts never publish, tag, push, or create a release. Shipping is a separate operation that verifies the exact built artifact, immutable tag, non-draft release, asset download, and published evidence boundary.

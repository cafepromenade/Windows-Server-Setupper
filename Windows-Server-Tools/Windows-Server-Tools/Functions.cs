using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Windows_Server_Tools
{
    public partial class MainWindow : Window
    {

        public static Task<OperationBatchResult> SolveWindowsTasks(string checkpointFile = null)
        {
            string netshPath = QuoteCommandPath(GetTrustedSystemExecutable("netsh.exe"));
            string powerCfgPath = QuoteCommandPath(GetTrustedSystemExecutable("powercfg.exe"));
            string regPath = QuoteCommandPath(GetTrustedSystemExecutable("reg.exe"));
            string taskKillPath = QuoteCommandPath(GetTrustedSystemExecutable("taskkill.exe"));
            string explorerPath = QuoteCommandPath(GetTrustedWindowsExecutable("explorer.exe"));
            string powerShellPath = QuoteCommandPath(GetTrustedPowerShellExecutable());
            var operations = new[]
            {
                new RecoverableOperation(
                    "Apply baseline server settings",
                    () => ExternalProcessRunner.RunCommandScriptAsync(
                        "Apply baseline server settings",
                        "@echo off\r\n"
                        + netshPath + " advfirewall set allprofiles state off\r\n"
                        + powerCfgPath + " -change -standby-timeout-ac 0\r\n"
                        + powerCfgPath + " -change -monitor-timeout-ac 0\r\n"
                        + powerCfgPath + " -change -disk-timeout-ac 0\r\n"
                        + powerCfgPath + " -change -hibernate-timeout-ac 0\r\n"
                        + regPath + " add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server\" /v fDenyTSConnections /t REG_DWORD /d 0 /f\r\n"
                        + regPath + " add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server\\WinStations\\RDP-Tcp\" /v UserAuthentication /t REG_DWORD /d 0 /f\r\n"
                        + regPath + " add \"HKLM\\SOFTWARE\\Microsoft\\Active Setup\\Installed Components\\{A509B1A7-37EF-4b3f-8CFC-4F3A74704073}\" /v IsInstalled /t REG_DWORD /d 0 /f\r\n"
                        + regPath + " add \"HKLM\\SOFTWARE\\Microsoft\\Active Setup\\Installed Components\\{A509B1A8-37EF-4b3f-8CFC-4F3A74704073}\" /v IsInstalled /t REG_DWORD /d 0 /f\r\n"
                        + taskKillPath + " /F /IM explorer.exe >NUL 2>&1 || ver >NUL\r\n"
                        + explorerPath + "\r\n"
                        + regPath + " add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\" /v EnableSmartScreen /t REG_DWORD /d 0 /f\r\n"
                        + powerShellPath + " -NoLogo -NoProfile -NonInteractive -Command \"$ErrorActionPreference='Stop'; Set-MpPreference -DisableRealtimeMonitoring $true\"\r\n"),
                    maxAttempts: 2,
                    retrySafety: RetrySafety.Idempotent),
                new RecoverableOperation(
                    "Install and configure DNS and DHCP roles",
                    () => RunPowerShellScriptAsync(@"
$dns = Get-WindowsFeature -Name DNS;
if (-not $dns.Installed) {
    $dnsResult = Install-WindowsFeature -Name DNS -IncludeManagementTools;
    if (-not $dnsResult.Success) { throw 'DNS Server did not install successfully.'; }
}
$dhcp = Get-WindowsFeature -Name DHCP;
if (-not $dhcp.Installed) {
    $dhcpResult = Install-WindowsFeature -Name DHCP -IncludeManagementTools;
    if (-not $dhcpResult.Success) { throw 'DHCP Server did not install successfully.'; }
}
$computerName = (Get-ComputerInfo).CsName;
$address = Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.AddressState -eq 'Preferred' -and $_.IPAddress -notlike '127.*' } |
    Select-Object -First 1 -ExpandProperty IPAddress;
if (-not $address) { throw 'No usable IPv4 address was found for DHCP authorization.'; }
if (-not (Get-DhcpServerInDC -DnsName $computerName -ErrorAction SilentlyContinue)) {
    Add-DhcpServerInDC -DnsName $computerName -IPAddress $address;
}"),
                    maxAttempts: 2,
                    retrySafety: RetrySafety.Idempotent),
                new RecoverableOperation(
                    "Configure the secure-attention sequence",
                    () => RunPowerShellScriptAsync(
                        "& " + ToPowerShellSingleQuotedLiteral(GetTrustedSystemExecutable("reg.exe"))
                        + " add 'HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System' /v DisableCAD /t REG_DWORD /d 1 /f\r\n"),
                    maxAttempts: 2,
                    retrySafety: RetrySafety.Idempotent),
                new RecoverableOperation(
                    "Install Chocolatey",
                    InstallChocolateyAsync,
                    maxAttempts: 3,
                    retrySafety: RetrySafety.Idempotent)
            };

            return RecoveryRunner.RunAllAsync(operations, checkpointFile);
        }

        public static async Task InstallChocolateyAsync()
        {
            const string packageUri = "https://github.com/chocolatey/choco/releases/download/2.7.3/chocolatey.2.7.3.nupkg";
            const string packageSha256 = "40778CC59245B3EB6EA5147AEEF5BEA5D577419E5ABCE22A224189740DC16DB5";
            const string installerSha256 = "C46903CFED1D74620630D0653CE057B3079AF5789AFEB1A5F884298A8693B4EC";
            const string installedExecutableSha256 = "4A1C6CF52929DD0348F5C91CE2A69A7D35A06A4C143957F42D855756DA4AF510";
            const string expectedVersion = "2.7.3";
            string commonApplicationData = GetTrustedCommonApplicationDataDirectory();
            string chocolateyRoot = EnsureTrustedChocolateyInstallRoot(commonApplicationData);
            string executable = Path.Combine(chocolateyRoot, "bin", "choco.exe");
            ValidateExistingChocolateyInstallParents(
                commonApplicationData,
                chocolateyRoot,
                executable);
            if (File.Exists(executable))
            {
                await VerifyChocolateyInstallationAsync(
                    executable,
                    expectedVersion,
                    installedExecutableSha256).ConfigureAwait(true);
                return;
            }

            string stageRoot = GetChocolateyStageRoot();
            string stageId;
            string ownershipNonce;
            string stagePath = CreateProtectedChocolateyStage(
                stageRoot,
                out stageId,
                out ownershipNonce);
            string archivePath = Path.Combine(stagePath, "chocolatey.2.7.3.nupkg");
            string expandedPath = Path.Combine(stagePath, "package");
            string installerPath = Path.Combine(expandedPath, "tools", "chocolateyInstall.ps1");
            bool installed = false;
            string phase = "create protected staging directory";
            string actualArchiveSha256 = "not computed";
            string actualInstallerSha256 = "not computed";

            try
            {
                ValidateOwnedChocolateyStage(
                    stageRoot,
                    stagePath,
                    stageId,
                    ownershipNonce);
                phase = "download pinned package";
                await DownloadPinnedPackageAsync(new Uri(packageUri), archivePath).ConfigureAwait(true);
                phase = "verify pinned package";
                actualArchiveSha256 = ComputeChocolateyFileSha256(archivePath);
                VerifyDigestValue(
                    actualArchiveSha256,
                    packageSha256,
                    "The pinned Chocolatey package did not match its SHA-256 digest.");

                phase = "extract pinned package";
                Directory.CreateDirectory(expandedPath);
                ExtractPinnedPackage(archivePath, expandedPath);
                ValidateProtectedStageTree(stagePath, installerPath);

                phase = "verify pinned installer";
                actualInstallerSha256 = ComputeChocolateyFileSha256(installerPath);
                VerifyDigestValue(
                    actualInstallerSha256,
                    installerSha256,
                    "The pinned Chocolatey installer did not match its SHA-256 digest.");
                ValidateMachineOnlyDirectory(stageRoot, true);
                ValidateMachineOnlyDirectory(stagePath, true);
                ValidateProtectedStageTree(stagePath, installerPath);
                ValidateOwnedChocolateyStage(
                    stageRoot,
                    stagePath,
                    stageId,
                    ownershipNonce);

                using (FileStream archiveLock = OpenVerifiedFileForRead(archivePath, packageSha256))
                using (FileStream installerLock = OpenVerifiedFileForRead(installerPath, installerSha256))
                {
                    phase = "execute pinned installer";
                    await RunPowerShellScriptAsync(
                        "& " + ToPowerShellSingleQuotedLiteral(installerPath) + ";\r\n"
                        + "if (-not (Test-Path -LiteralPath " + ToPowerShellSingleQuotedLiteral(executable) + " -PathType Leaf)) {\r\n"
                        + "    throw 'Chocolatey did not create its executable.';\r\n"
                        + "}").ConfigureAwait(true);
                }

                phase = "verify Chocolatey installation";
                await VerifyChocolateyInstallationAsync(
                    executable,
                    expectedVersion,
                    installedExecutableSha256).ConfigureAwait(true);
                installed = true;
            }
            catch (Exception error)
            {
                WriteChocolateyFailureEvidence(
                    stagePath,
                    phase,
                    packageSha256,
                    actualArchiveSha256,
                    installerSha256,
                    actualInstallerSha256,
                    error);
                ErrorLog.Write("Install Chocolatey", error);
                throw;
            }
            finally
            {
                if (installed)
                {
                    TryDeleteOwnedChocolateyStage(
                        stageRoot,
                        stagePath,
                        stageId,
                        ownershipNonce);
                }
            }
        }

        private static string GetChocolateyStageRoot()
        {
            return Path.GetFullPath(Path.Combine(
                GetTrustedCommonApplicationDataDirectory(),
                "WindowsServerToolsSecureStaging"));
        }

        private static string GetTrustedCommonApplicationDataDirectory()
        {
            string commonApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(commonApplicationData)
                || !Path.IsPathRooted(commonApplicationData))
            {
                throw new InvalidOperationException("The per-machine application-data directory is unavailable.");
            }

            ValidateDirectoryPathHasNoReparsePoints(commonApplicationData);
            return Path.GetFullPath(commonApplicationData);
        }

        private static string EnsureTrustedChocolateyInstallRoot(
            string commonApplicationData)
        {
            string commonRoot = Path.GetFullPath(commonApplicationData).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            ValidateSharedCommonApplicationDataDirectory(commonRoot);

            string chocolateyRoot = Path.GetFullPath(Path.Combine(
                commonRoot,
                "chocolatey"));
            string expectedRoot = commonRoot + Path.DirectorySeparatorChar + "chocolatey";
            if (!string.Equals(chocolateyRoot, expectedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The Chocolatey installation directory did not resolve to its canonical location.");
            }

            if (File.Exists(chocolateyRoot))
            {
                throw new InvalidOperationException(
                    "The Chocolatey installation directory is occupied by a file. Remove it only after an administrator verifies that it is safe.");
            }

            var directory = new DirectoryInfo(chocolateyRoot);
            if (!directory.Exists)
            {
                directory.Create(CreateMachineOnlyDirectorySecurity());
            }

            ValidateTrustedChocolateyDirectory(chocolateyRoot, true);
            return chocolateyRoot;
        }

        private static void ValidateExistingChocolateyInstallParents(
            string commonApplicationData,
            string chocolateyRoot,
            string executablePath)
        {
            string commonRoot = Path.GetFullPath(commonApplicationData).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string installedRoot = Path.GetFullPath(chocolateyRoot).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string expectedRoot = Path.Combine(commonRoot, "chocolatey");
            string expectedExecutable = Path.Combine(installedRoot, "bin", "choco.exe");
            string executable = Path.GetFullPath(executablePath);
            if (!string.Equals(installedRoot, expectedRoot, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(executable, expectedExecutable, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The Chocolatey executable did not resolve to its canonical installed path.");
            }

            ValidateSharedCommonApplicationDataDirectory(commonRoot);
            ValidateTrustedChocolateyDirectory(installedRoot, true);

            string binPath = Path.Combine(installedRoot, "bin");
            if (File.Exists(binPath))
            {
                throw new InvalidOperationException(
                    "The Chocolatey bin directory is occupied by a file. Remove it only after an administrator verifies that it is safe.");
            }

            if (Directory.Exists(binPath))
            {
                ValidateTrustedChocolateyDirectory(binPath, false);
            }

            if (Directory.Exists(executable))
            {
                throw new InvalidOperationException(
                    "The Chocolatey executable path is occupied by a directory. Remove it only after an administrator verifies that it is safe.");
            }
        }

        private static void ValidateSharedCommonApplicationDataDirectory(string directoryPath)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(directoryPath));
            if (!directory.Exists
                || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "The per-machine application-data directory is not a trusted local directory.");
            }

            ValidateTrustedChocolateyAcl(
                directory.GetAccessControl(
                    AccessControlSections.Access | AccessControlSections.Owner),
                true,
                true);
        }

        private static void ValidateTrustedChocolateyDirectory(
            string directoryPath,
            bool requireProtectedAcl)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(directoryPath));
            if (!directory.Exists
                || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "The Chocolatey installation path contains a missing directory or reparse point.");
            }

            ValidateTrustedChocolateyAcl(
                directory.GetAccessControl(
                    AccessControlSections.Access | AccessControlSections.Owner),
                requireProtectedAcl,
                false);
        }

        private static void ValidateTrustedChocolateyFile(string filePath)
        {
            var file = new FileInfo(Path.GetFullPath(filePath));
            if (!file.Exists
                || (file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidOperationException(
                    "The Chocolatey executable is missing, not a regular file, or is a reparse point.");
            }

            ValidateTrustedChocolateyAcl(
                file.GetAccessControl(
                    AccessControlSections.Access | AccessControlSections.Owner),
                false,
                false);
        }

        private static void ValidateTrustedChocolateyAcl(
            FileSystemSecurity security,
            bool requireProtectedAcl,
            bool sharedCommonApplicationDataRoot)
        {
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var administrators = new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid,
                null);
            var owner = (SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier));
            if (!owner.Equals(system) && !owner.Equals(administrators))
            {
                throw new InvalidOperationException(
                    "A Chocolatey installation path has an owner other than Administrators or SYSTEM.");
            }

            if (requireProtectedAcl && !security.AreAccessRulesProtected)
            {
                throw new InvalidOperationException(
                    "A Chocolatey installation path inherits access rules at a protected boundary.");
            }

            FileSystemRights readOnlyRights = FileSystemRights.ReadAndExecute
                | FileSystemRights.Read
                | FileSystemRights.ReadAttributes
                | FileSystemRights.ReadExtendedAttributes
                | FileSystemRights.ReadPermissions
                | FileSystemRights.Synchronize;
            FileSystemRights sharedRootForbiddenRights = FileSystemRights.Delete
                | FileSystemRights.DeleteSubdirectoriesAndFiles
                | FileSystemRights.ChangePermissions
                | FileSystemRights.TakeOwnership;

            foreach (FileSystemAccessRule rule in security.GetAccessRules(
                true,
                true,
                typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow)
                {
                    continue;
                }

                var identity = (SecurityIdentifier)rule.IdentityReference;
                if (identity.Equals(system) || identity.Equals(administrators))
                {
                    continue;
                }

                if (sharedCommonApplicationDataRoot)
                {
                    if ((rule.FileSystemRights & sharedRootForbiddenRights) != 0)
                    {
                        throw new InvalidOperationException(
                            "The per-machine application-data directory grants an untrusted identity delete or permission-control rights.");
                    }

                    continue;
                }

                FileSystemRights unexpectedRights = rule.FileSystemRights & ~readOnlyRights;
                if (unexpectedRights != 0)
                {
                    throw new InvalidOperationException(
                        "A Chocolatey installation path grants an untrusted identity write, create, delete, or permission-control rights.");
                }
            }
        }

        private static void ValidateDirectoryPathHasNoReparsePoints(string directoryPath)
        {
            var current = new DirectoryInfo(Path.GetFullPath(directoryPath));
            while (current != null)
            {
                if (!current.Exists)
                {
                    throw new DirectoryNotFoundException(
                        "A required local directory does not exist.");
                }

                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "A required local directory path contains a reparse point.");
                }

                current = current.Parent;
            }
        }

        private static string CreateProtectedChocolateyStage(
            string stageRoot,
            out string stageId,
            out string ownershipNonce)
        {
            DirectorySecurity security = CreateMachineOnlyDirectorySecurity();
            var root = new DirectoryInfo(stageRoot);
            if (!root.Exists)
            {
                root.Create(security);
            }

            ValidateMachineOnlyDirectory(stageRoot, true);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                stageId = Guid.NewGuid().ToString("N");
                ownershipNonce = Guid.NewGuid().ToString("N");
                string stagePath = Path.Combine(
                    stageRoot,
                    "chocolatey-" + stageId);
                var stage = new DirectoryInfo(stagePath);
                if (stage.Exists)
                {
                    continue;
                }

                stage.Create(security);
                ValidateMachineOnlyDirectory(stagePath, true);
                File.WriteAllText(
                    GetChocolateyOwnershipMarkerPath(stagePath),
                    ownershipNonce,
                    new UTF8Encoding(false));
                ValidateOwnedChocolateyStage(
                    stageRoot,
                    stagePath,
                    stageId,
                    ownershipNonce);
                return stagePath;
            }

            stageId = null;
            ownershipNonce = null;
            throw new IOException("A unique protected Chocolatey staging directory could not be created.");
        }

        private static DirectorySecurity CreateMachineOnlyDirectorySecurity()
        {
            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var administrators = new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid,
                null);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.SetOwner(administrators);
            security.AddAccessRule(new FileSystemAccessRule(
                system,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                administrators,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            return security;
        }

        private static void ValidateMachineOnlyDirectory(
            string directoryPath,
            bool requireProtectedAcl)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(directoryPath));
            if (!directory.Exists)
            {
                throw new DirectoryNotFoundException(
                    "The protected staging directory no longer exists.");
            }

            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "The protected staging directory cannot be a reparse point.");
            }

            DirectorySecurity security = directory.GetAccessControl(
                AccessControlSections.Access | AccessControlSections.Owner);
            if (requireProtectedAcl && !security.AreAccessRulesProtected)
            {
                throw new InvalidOperationException(
                    "The protected staging directory inherits access rules.");
            }

            var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var administrators = new SecurityIdentifier(
                WellKnownSidType.BuiltinAdministratorsSid,
                null);
            var owner = (SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier));
            if (requireProtectedAcl
                && !owner.Equals(system)
                && !owner.Equals(administrators))
            {
                throw new InvalidOperationException(
                    "The protected staging directory has an unexpected owner.");
            }

            bool systemHasFullControl = false;
            bool administratorsHaveFullControl = false;

            foreach (FileSystemAccessRule rule in security.GetAccessRules(
                true,
                true,
                typeof(SecurityIdentifier)))
            {
                var identity = (SecurityIdentifier)rule.IdentityReference;
                bool trustedIdentity = identity.Equals(system)
                    || identity.Equals(administrators);
                if (!trustedIdentity || rule.AccessControlType != AccessControlType.Allow)
                {
                    throw new InvalidOperationException(
                        "The protected staging directory grants an unexpected access rule.");
                }

                bool hasFullControl = (rule.FileSystemRights & FileSystemRights.FullControl)
                    == FileSystemRights.FullControl;
                if (identity.Equals(system) && hasFullControl)
                {
                    systemHasFullControl = true;
                }
                else if (identity.Equals(administrators) && hasFullControl)
                {
                    administratorsHaveFullControl = true;
                }
            }

            if (!systemHasFullControl || !administratorsHaveFullControl)
            {
                throw new InvalidOperationException(
                    "The protected staging directory is missing an administrator or SYSTEM access rule.");
            }
        }

        private static async Task DownloadPinnedPackageAsync(Uri source, string destination)
        {
            const long maximumPackageBytes = 64L * 1024L * 1024L;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            using (var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                UseCookies = false
            })
            using (var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            })
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
            using (HttpResponseMessage response = await client.GetAsync(
                source,
                HttpCompletionOption.ResponseHeadersRead,
                cancellation.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                Uri finalAddress = response.RequestMessage == null
                    ? null
                    : response.RequestMessage.RequestUri;
                if (finalAddress == null
                    || !string.Equals(finalAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The pinned Chocolatey package did not resolve to an HTTPS address.");
                }

                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > maximumPackageBytes)
                {
                    throw new InvalidDataException(
                        "The pinned Chocolatey package exceeds the download size limit.");
                }

                using (Stream sourceStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var destinationStream = new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    true))
                {
                    var buffer = new byte[81920];
                    long totalBytes = 0;
                    try
                    {
                        int bytesRead;
                        while ((bytesRead = await sourceStream.ReadAsync(
                            buffer,
                            0,
                            buffer.Length,
                            cancellation.Token).ConfigureAwait(false)) > 0)
                        {
                            totalBytes += bytesRead;
                            if (totalBytes > maximumPackageBytes)
                            {
                                throw new InvalidDataException(
                                    "The pinned Chocolatey package exceeds the download size limit.");
                            }

                            await destinationStream.WriteAsync(
                                buffer,
                                0,
                                bytesRead,
                                cancellation.Token).ConfigureAwait(false);
                        }

                        await destinationStream.FlushAsync(cancellation.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        Array.Clear(buffer, 0, buffer.Length);
                    }
                }
            }
        }

        private static string ComputeChocolateyFileSha256(string filePath)
        {
            using (FileStream stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                return BitConverter.ToString(algorithm.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }

        private static void ExtractPinnedPackage(string archivePath, string destinationPath)
        {
            const int maximumEntries = 4096;
            const long maximumExpandedBytes = 512L * 1024L * 1024L;
            string destination = Path.GetFullPath(destinationPath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string destinationPrefix = destination + Path.DirectorySeparatorChar;
            int entryCount = 0;
            long expandedBytes = 0;

            using (var archiveStream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            using (var archive = new ZipArchive(
                archiveStream,
                ZipArchiveMode.Read,
                false))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    entryCount++;
                    if (entryCount > maximumEntries)
                    {
                        throw new InvalidDataException(
                            "The pinned Chocolatey package contains too many entries.");
                    }

                    int unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (unixFileType == 0xA000)
                    {
                        throw new InvalidDataException(
                            "The pinned Chocolatey package contains a symbolic link.");
                    }

                    string relativeName = (entry.FullName ?? string.Empty)
                        .Replace('/', Path.DirectorySeparatorChar);
                    if (string.IsNullOrWhiteSpace(relativeName))
                    {
                        continue;
                    }

                    string outputPath = Path.GetFullPath(Path.Combine(destination, relativeName));
                    if (!outputPath.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "The pinned Chocolatey package contains a path outside its staging directory.");
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(outputPath);
                        continue;
                    }

                    expandedBytes += entry.Length;
                    if (expandedBytes > maximumExpandedBytes)
                    {
                        throw new InvalidDataException(
                            "The pinned Chocolatey package exceeds the extraction size limit.");
                    }

                    string outputDirectory = Path.GetDirectoryName(outputPath);
                    if (string.IsNullOrWhiteSpace(outputDirectory))
                    {
                        throw new InvalidDataException(
                            "The pinned Chocolatey package contains an invalid entry path.");
                    }

                    Directory.CreateDirectory(outputDirectory);
                    using (Stream input = entry.Open())
                    using (var output = new FileStream(
                        outputPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        var buffer = new byte[81920];
                        try
                        {
                            int bytesRead;
                            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                output.Write(buffer, 0, bytesRead);
                            }

                            output.Flush(true);
                        }
                        finally
                        {
                            Array.Clear(buffer, 0, buffer.Length);
                        }
                    }
                }
            }
        }

        private static void VerifyDigestValue(
            string actualSha256,
            string expectedSha256,
            string mismatchMessage)
        {
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(mismatchMessage);
            }
        }

        private static FileStream OpenVerifiedFileForRead(
            string filePath,
            string expectedSha256)
        {
            var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            try
            {
                using (SHA256 algorithm = SHA256.Create())
                {
                    string actualSha256 = BitConverter.ToString(algorithm.ComputeHash(stream))
                        .Replace("-", string.Empty);
                    if (!string.Equals(
                        actualSha256,
                        expectedSha256,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "A protected Chocolatey staging file changed before execution.");
                    }
                }

                stream.Position = 0;
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private static void ValidateProtectedStageTree(string stagePath, string installerPath)
        {
            string stage = Path.GetFullPath(stagePath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string installer = Path.GetFullPath(installerPath);
            string prefix = stage + Path.DirectorySeparatorChar;
            if (!installer.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The Chocolatey installer resolved outside the protected staging directory.");
            }

            ValidateMachineOnlyDirectory(stage, true);
            string current = Path.GetDirectoryName(installer);
            while (!string.IsNullOrWhiteSpace(current)
                && current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                ValidateMachineOnlyDirectory(current, false);
                if (string.Equals(current, stage, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = Path.GetDirectoryName(current);
            }

            var installerFile = new FileInfo(installer);
            if (!installerFile.Exists || installerFile.Length == 0)
            {
                throw new InvalidDataException(
                    "The pinned Chocolatey package did not contain its installer.");
            }

            if ((installerFile.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "The Chocolatey installer cannot be a reparse point.");
            }
        }

        private static string GetChocolateyOwnershipMarkerPath(string stagePath)
        {
            return Path.Combine(stagePath, "owner.marker");
        }

        private static void ValidateOwnedChocolateyStage(
            string stageRoot,
            string stagePath,
            string stageId,
            string ownershipNonce)
        {
            if (string.IsNullOrWhiteSpace(stageId)
                || string.IsNullOrWhiteSpace(ownershipNonce))
            {
                throw new InvalidOperationException(
                    "The Chocolatey staging ownership proof is unavailable.");
            }

            string root = Path.GetFullPath(stageRoot).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string stage = Path.GetFullPath(stagePath).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string expectedStage = Path.Combine(root, "chocolatey-" + stageId);
            if (!string.Equals(stage, expectedStage, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    Path.GetDirectoryName(stage),
                    root,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The Chocolatey staging path does not match its ownership proof.");
            }

            ValidateMachineOnlyDirectory(root, true);
            ValidateMachineOnlyDirectory(stage, true);
            string markerPath = GetChocolateyOwnershipMarkerPath(stage);
            var marker = new FileInfo(markerPath);
            if (!marker.Exists
                || (marker.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidOperationException(
                    "The Chocolatey staging ownership marker is unavailable.");
            }

            string markerValue = File.ReadAllText(markerPath, Encoding.UTF8);
            if (!string.Equals(markerValue, ownershipNonce, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The Chocolatey staging ownership marker did not match this attempt.");
            }
        }

        private static async Task VerifyChocolateyInstallationAsync(
            string executablePath,
            string expectedVersion)
        {
            string commonApplicationData = GetTrustedCommonApplicationDataDirectory();
            string chocolateyRoot = Path.GetFullPath(Path.Combine(
                commonApplicationData,
                "chocolatey"));
            string executable = Path.GetFullPath(executablePath);
            string prefix = chocolateyRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!executable.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The Chocolatey executable resolved outside its expected installation directory.");
            }

            var executableFile = new FileInfo(executable);
            if (!executableFile.Exists
                || (executableFile.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new InvalidOperationException(
                    "Chocolatey did not create a regular executable file.");
            }

            string current = executableFile.DirectoryName;
            while (!string.IsNullOrWhiteSpace(current)
                && current.StartsWith(chocolateyRoot, StringComparison.OrdinalIgnoreCase))
            {
                var directory = new DirectoryInfo(current);
                if (!directory.Exists
                    || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "The Chocolatey installation path contains a reparse point.");
                }

                if (string.Equals(current, chocolateyRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = directory.Parent == null ? null : directory.Parent.FullName;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            ExternalProcessResult result = await ExternalProcessRunner.RunAsync(
                "Verify Chocolatey version",
                startInfo,
                TimeSpan.FromMinutes(1)).ConfigureAwait(true);
            if (!string.Equals(
                result.StandardOutput.Trim(),
                expectedVersion,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Chocolatey reported an unexpected installed version.");
            }
        }

        private static void WriteChocolateyFailureEvidence(
            string stagePath,
            string phase,
            string expectedArchiveSha256,
            string actualArchiveSha256,
            string expectedInstallerSha256,
            string actualInstallerSha256,
            Exception error)
        {
            try
            {
                ValidateMachineOnlyDirectory(stagePath, true);
                string evidencePath = Path.Combine(stagePath, "failure.txt");
                File.WriteAllText(
                    evidencePath,
                    "Chocolatey installation failed.\r\n"
                    + "UTC: " + DateTimeOffset.UtcNow.ToString("O") + "\r\n"
                    + "Phase: " + (phase ?? "unknown") + "\r\n"
                    + "Expected package SHA-256: " + expectedArchiveSha256 + "\r\n"
                    + "Observed package SHA-256: " + actualArchiveSha256 + "\r\n"
                    + "Expected installer SHA-256: " + expectedInstallerSha256 + "\r\n"
                    + "Observed installer SHA-256: " + actualInstallerSha256 + "\r\n"
                    + "Exception type: " + (error == null ? "unknown" : error.GetType().FullName) + "\r\n"
                    + "Exception HResult: " + (error == null ? "unknown" : error.HResult.ToString()) + "\r\n"
                    + "The protected staging directory was retained for administrator review.\r\n"
                    + "Detailed bounded diagnostics are in the application error log.\r\n",
                    new UTF8Encoding(false));
            }
            catch (Exception evidenceError)
            {
                ErrorLog.Write("Record Chocolatey failure evidence", evidenceError);
            }
        }

        private static void TryDeleteOwnedChocolateyStage(
            string stageRoot,
            string stagePath,
            string stageId,
            string ownershipNonce)
        {
            try
            {
                ValidateOwnedChocolateyStage(
                    stageRoot,
                    stagePath,
                    stageId,
                    ownershipNonce);
                ValidateTreeContainsNoReparsePoints(stagePath);
                Directory.Delete(stagePath, true);
            }
            catch (Exception cleanupError)
            {
                ErrorLog.Write("Remove protected Chocolatey staging directory", cleanupError);
                WriteChocolateyCleanupFailureEvidence(stagePath, cleanupError);
            }
        }

        private static void ValidateTreeContainsNoReparsePoints(string rootPath)
        {
            const int maximumEntries = 10000;
            int entryCount = 0;
            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(rootPath));

            while (pending.Count > 0)
            {
                string directoryPath = pending.Pop();
                foreach (string entryPath in Directory.EnumerateFileSystemEntries(directoryPath))
                {
                    entryCount++;
                    if (entryCount > maximumEntries)
                    {
                        throw new InvalidOperationException(
                            "The Chocolatey staging directory exceeds the cleanup entry limit.");
                    }

                    FileAttributes attributes = File.GetAttributes(entryPath);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            "The Chocolatey staging directory contains a reparse point.");
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entryPath);
                    }
                }
            }
        }

        private static void WriteChocolateyCleanupFailureEvidence(
            string stagePath,
            Exception cleanupError)
        {
            try
            {
                ValidateMachineOnlyDirectory(stagePath, true);
                File.WriteAllText(
                    Path.Combine(stagePath, "cleanup-failure.txt"),
                    "Chocolatey installed, but protected staging cleanup failed.\r\n"
                    + "UTC: " + DateTimeOffset.UtcNow.ToString("O") + "\r\n"
                    + "Exception type: "
                    + (cleanupError == null ? "unknown" : cleanupError.GetType().FullName)
                    + "\r\nException HResult: "
                    + (cleanupError == null ? "unknown" : cleanupError.HResult.ToString())
                    + "\r\nThe exact protected staging directory was retained for administrator review.\r\n",
                    new UTF8Encoding(false));
            }
            catch (Exception evidenceError)
            {
                ErrorLog.Write("Record Chocolatey cleanup failure evidence", evidenceError);
            }
        }

        public static Task SetCurrentAddressStaticAsync()
        {
            return RunPowerShellScriptAsync(@"
$configuration = Get-NetIPConfiguration |
    Where-Object { $_.IPv4Address -and $_.IPv4DefaultGateway } |
    Select-Object -First 1;
if (-not $configuration) { throw 'No active IPv4 adapter with a default gateway was found.'; }
$interfaceIndex = $configuration.InterfaceIndex;
$address = $configuration.IPv4Address.IPAddress;
$prefixLength = $configuration.IPv4Address.PrefixLength;
$gateway = $configuration.IPv4DefaultGateway.NextHop;
if (-not $address -or -not $prefixLength -or -not $gateway) {
    throw 'The active IPv4 configuration is incomplete.';
}
Set-NetIPInterface -InterfaceIndex $interfaceIndex -Dhcp Disabled;
$matchingAddress = Get-NetIPAddress -InterfaceIndex $interfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -eq $address -and $_.PrefixLength -eq $prefixLength };
if (-not $matchingAddress) {
    Get-NetIPAddress -InterfaceIndex $interfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.PrefixOrigin -ne 'WellKnown' } |
        Remove-NetIPAddress -Confirm:$false;
    New-NetIPAddress -InterfaceIndex $interfaceIndex -IPAddress $address -PrefixLength $prefixLength -DefaultGateway $gateway;
}
Set-DnsClientServerAddress -InterfaceIndex $interfaceIndex -ServerAddresses $gateway;
");
        }
        public static void SetStaticIp(string adapterName, string ipAddress, string subnetMask)
        {
            if (string.IsNullOrWhiteSpace(adapterName)
                || adapterName.Length > 256
                || adapterName.IndexOf('"') >= 0
                || adapterName.IndexOf('\\') >= 0
                || adapterName.Any(char.IsControl))
            {
                throw new ArgumentException(
                    "Enter a valid network adapter name.",
                    nameof(adapterName));
            }

            string validatedAddress = ValidateIpv4Address(ipAddress, nameof(ipAddress));
            string validatedSubnetMask = ValidateIpv4Address(subnetMask, nameof(subnetMask));
            string defaultGateway = GetDefaultGateway();
            if (string.IsNullOrEmpty(defaultGateway))
            {
                throw new InvalidOperationException("No default gateway was found, so a static address could not be configured.");
            }

            string validatedGateway = ValidateIpv4Address(defaultGateway, "defaultGateway");
            string adapterArgument = "\"" + adapterName + "\"";
            string setIpCommand = $"interface ip set address name={adapterArgument} static {validatedAddress} {validatedSubnetMask} {validatedGateway}";
            string setDnsCommand = $"interface ip set dns name={adapterArgument} static {validatedGateway}";

            ExecuteCommand(setIpCommand);
            ExecuteCommand(setDnsCommand);
        }

        private static string ValidateIpv4Address(string value, string parameterName)
        {
            IPAddress parsed;
            if (!IPAddress.TryParse(value, out parsed)
                || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            {
                throw new ArgumentException(
                    "Enter a valid IPv4 address.",
                    parameterName);
            }

            return parsed.ToString();
        }

        private static string GetDefaultGateway()
        {
            var gateways = NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(ni => ni.GetIPProperties().GatewayAddresses)
                .Where(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(g => g.Address.ToString())
                .ToList();

            return gateways.FirstOrDefault();
        }

        private static void ExecuteCommand(string command)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = GetTrustedSystemExecutable("netsh.exe"),
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                ExternalProcessRunner.RunAsync(
                    "Configure the static network address",
                    processStartInfo,
                    TimeSpan.FromMinutes(2)).GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                ErrorLog.Write("Configure the static network address", error);
                throw;
            }
        }



        public static async Task InstallActiveDirectoryAndPromoteToDC(
            string domainName,
            SecureString safeModeAdminPassword,
            string domainNetbiosName = "CONTOSO")
        {
            if (!TryValidateDomainName(domainName))
            {
                throw new ArgumentException("Enter a fully qualified domain name such as example.local.", nameof(domainName));
            }

            if (!TryValidateNetbiosName(domainNetbiosName))
            {
                throw new ArgumentException("The NetBIOS name must contain 1 to 15 letters, numbers, or hyphens.", nameof(domainNetbiosName));
            }

            if (safeModeAdminPassword == null || safeModeAdminPassword.Length == 0)
            {
                throw new ArgumentNullException(nameof(safeModeAdminPassword));
            }

            string domainLiteral = ToPowerShellSingleQuotedLiteral(domainName);
            string netbiosLiteral = ToPowerShellSingleQuotedLiteral(domainNetbiosName);

            string installADDSCommand = @"
            $feature = Get-WindowsFeature -Name AD-Domain-Services;
            if (-not $feature.Installed) {
                $result = Install-WindowsFeature -Name AD-Domain-Services -IncludeManagementTools;
                if (-not $result.Success) {
                    throw 'Active Directory Domain Services did not install successfully.';
                }
            }
        ";

            string promoteCommand = $@"
            Import-Module ADDSDeployment;
            $computerSystem = Get-CimInstance Win32_ComputerSystem;
            if ($computerSystem.DomainRole -ge 4) {{
                Import-Module ActiveDirectory;
                $existingDomain = (Get-ADDomain).DNSRoot;
                if ($existingDomain -ne {domainLiteral}) {{
                    throw ""This server is already a domain controller for $existingDomain, not the requested domain."";
                }}
            }} else {{
                $passwordLength = 0;
                $nextCharacter = 0;
                $character = [char]0;
                $safeModePassword = $null;
                try {{
                    $safeModePassword = New-Object Security.SecureString;
                    while (($nextCharacter = [Console]::In.Read()) -ne -1) {{
                        if ($passwordLength -ge 65536) {{
                            throw 'The safe-mode password exceeds the accepted input limit.';
                        }}
                        $character = [char]$nextCharacter;
                        $safeModePassword.AppendChar($character);
                        $character = [char]0;
                        $passwordLength++;
                    }}
                    if ($passwordLength -eq 0) {{
                        throw 'The safe-mode password was not provided to the promotion process.';
                    }}
                    $safeModePassword.MakeReadOnly();
                    Install-ADDSForest `
                    -CreateDnsDelegation:$false `
                    -DatabasePath ""C:\Windows\NTDS"" `
                    -DomainMode ""WinThreshold"" `
                    -DomainName {domainLiteral} `
                    -DomainNetbiosName {netbiosLiteral} `
                    -ForestMode ""WinThreshold"" `
                    -InstallDns:$true `
                    -LogPath ""C:\Windows\NTDS"" `
                    -NoRebootOnCompletion:$false `
                    -SysvolPath ""C:\Windows\SYSVOL"" `
                    -SafeModeAdministratorPassword $safeModePassword `
                    -Force:$true;
                }} finally {{
                    $passwordLength = 0;
                    $nextCharacter = 0;
                    $character = [char]0;
                    if ($null -ne $safeModePassword) {{
                        $safeModePassword.Dispose();
                        $safeModePassword = $null;
                    }}
                }}
            }}
        ";

            await RunPowerShellScriptAsync(installADDSCommand).ConfigureAwait(true);

            string domainFile = ProtectedWorkflowState.GetPath("State", "Domain.txt");
            ProtectedWorkflowState.WriteAllTextAtomic(domainFile, domainName);

            try
            {
                char[] passwordInput = CopySecureStringToCharacters(safeModeAdminPassword);
                try
                {
                    await RunPowerShellScriptAsync(promoteCommand, passwordInput).ConfigureAwait(true);
                }
                finally
                {
                    Array.Clear(passwordInput, 0, passwordInput.Length);
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(domainFile))
                    {
                        File.Delete(domainFile);
                    }
                }
                catch (Exception cleanupError)
                {
                    ErrorLog.Write("Remove incomplete domain state", cleanupError);
                }

                throw;
            }
        }

        private static bool TryValidateDomainName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 253)
            {
                return false;
            }

            string[] labels = value.Split('.');
            return labels.Length >= 2
                && labels.All(label => label.Length >= 1
                    && label.Length <= 63
                    && char.IsLetterOrDigit(label[0])
                    && char.IsLetterOrDigit(label[label.Length - 1])
                    && label.All(character => char.IsLetterOrDigit(character) || character == '-'));
        }

        private static bool TryValidateNetbiosName(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length <= 15
                && value.All(character => char.IsLetterOrDigit(character) || character == '-');
        }

        private static string ToPowerShellSingleQuotedLiteral(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
        }

        public static Task RunPowerShellScriptAsync(string script)
        {
            return RunPowerShellScriptAsync(script, null);
        }

        private static async Task RunPowerShellScriptAsync(
            string script,
            char[] standardInput)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                throw new ArgumentException("A PowerShell script is required.", nameof(script));
            }

            string guardedScript = "Set-StrictMode -Version 2;" + Environment.NewLine
                + "$ErrorActionPreference = 'Stop';" + Environment.NewLine
                + "$global:LASTEXITCODE = 0;" + Environment.NewLine
                + script
                + Environment.NewLine
                + "if ($null -ne $LASTEXITCODE -and $LASTEXITCODE -ne 0) { throw \"A native command exited with code $LASTEXITCODE.\" }";
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(guardedScript));
            var startInfo = new ProcessStartInfo
            {
                FileName = GetTrustedPowerShellExecutable(),
                Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (standardInput == null)
            {
                await ExternalProcessRunner.RunAsync(
                    "PowerShell",
                    startInfo,
                    TimeSpan.FromMinutes(60)).ConfigureAwait(true);
            }
            else
            {
                await ExternalProcessRunner.RunAsync(
                    "PowerShell",
                    startInfo,
                    TimeSpan.FromMinutes(60),
                    standardInput).ConfigureAwait(true);
            }
        }

        private static char[] CopySecureStringToCharacters(SecureString value)
        {
            IntPtr plaintext = IntPtr.Zero;
            char[] characters = null;
            try
            {
                plaintext = Marshal.SecureStringToGlobalAllocUnicode(value);
                characters = new char[value.Length];
                for (int index = 0; index < characters.Length; index++)
                {
                    characters[index] = (char)Marshal.ReadInt16(plaintext, index * sizeof(char));
                }

                return characters;
            }
            catch
            {
                if (characters != null)
                {
                    Array.Clear(characters, 0, characters.Length);
                }

                throw;
            }
            finally
            {
                if (plaintext != IntPtr.Zero)
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(plaintext);
                }
            }
        }

        private static string GetTrustedPowerShellExecutable()
        {
            return GetTrustedSystemExecutable(
                Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"));
        }

        private static string GetTrustedSystemExecutable(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                throw new ArgumentException(
                    "A relative System32 executable path is required.",
                    nameof(relativePath));
            }

            string windowsDirectory = GetTrustedWindowsDirectory();
            string systemDirectory = Path.GetFullPath(
                Path.Combine(windowsDirectory, "System32"));
            string candidate = Path.GetFullPath(Path.Combine(systemDirectory, relativePath));
            string prefix = systemDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The requested executable resolved outside System32.");
            }

            ValidateTrustedExecutablePath(candidate, systemDirectory);
            return candidate;
        }

        private static string GetTrustedWindowsExecutable(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || Path.IsPathRooted(fileName)
                || fileName.IndexOf(Path.DirectorySeparatorChar) >= 0
                || fileName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                throw new ArgumentException(
                    "A Windows-directory executable name is required.",
                    nameof(fileName));
            }

            string windowsDirectory = GetTrustedWindowsDirectory();
            string candidate = Path.GetFullPath(Path.Combine(windowsDirectory, fileName));
            ValidateTrustedExecutablePath(candidate, windowsDirectory);
            return candidate;
        }

        private static string GetTrustedWindowsDirectory()
        {
            string windowsDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windowsDirectory)
                || !Path.IsPathRooted(windowsDirectory))
            {
                throw new InvalidOperationException(
                    "The Windows directory is unavailable.");
            }

            string fullPath = Path.GetFullPath(windowsDirectory).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var directory = new DirectoryInfo(fullPath);
            if (!directory.Exists
                || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "The Windows directory is not a trusted local directory.");
            }

            return fullPath;
        }

        private static void ValidateTrustedExecutablePath(
            string executablePath,
            string trustedRoot)
        {
            var executable = new FileInfo(executablePath);
            if (!executable.Exists
                || (executable.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new FileNotFoundException(
                    "A trusted Windows executable is unavailable.",
                    executablePath);
            }

            string root = Path.GetFullPath(trustedRoot).TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            string current = executable.DirectoryName;
            while (!string.IsNullOrWhiteSpace(current)
                && current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                var directory = new DirectoryInfo(current);
                if (!directory.Exists
                    || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        "A trusted executable path contains a reparse point.");
                }

                if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = directory.Parent == null ? null : directory.Parent.FullName;
            }
        }

        private static string QuoteCommandPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.IndexOf('"') >= 0)
            {
                throw new ArgumentException("A valid executable path is required.", nameof(path));
            }

            return "\"" + path + "\"";
        }

        public static void RunPowerShellScript(string script)
        {
            RunPowerShellScriptAsync(script).GetAwaiter().GetResult();
        }
    }
}

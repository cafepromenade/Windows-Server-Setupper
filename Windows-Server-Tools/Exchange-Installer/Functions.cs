using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using UsefulTools;
using Task = System.Threading.Tasks.Task;

namespace Exchange_Installer
{
    public class Functions
    {
        public static async Task SolveWindowsTasks()
        {
            //await Chocolatey.InstallChocolatey();
            await RunPowerShellScript("Set-ExecutionPolicy Bypass -Scope Process -Force; [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))");

            await Command.RunCommandHidden("@echo off\r\n\r\n:: Disable all firewalls\r\nnetsh advfirewall set allprofiles state off\r\n\r\n:: Disable sleep and monitor off settings\r\npowercfg -change -standby-timeout-ac 0\r\npowercfg -change -monitor-timeout-ac 0\r\npowercfg -change -disk-timeout-ac 0\r\npowercfg -change -hibernate-timeout-ac 0\r\n\r\n:: Enable Remote Desktop without Network Level Authentication\r\nreg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server\" /v fDenyTSConnections /t REG_DWORD /d 0 /f\r\nreg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server\\WinStations\\RDP-Tcp\" /v UserAuthentication /t REG_DWORD /d 0 /f\r\n\r\n:: Disable Internet Explorer Enhanced Security Configuration (IE ESC)\r\nreg add \"HKLM\\SOFTWARE\\Microsoft\\Active Setup\\Installed Components\\{A509B1A7-37EF-4b3f-8CFC-4F3A74704073}\" /v IsInstalled /t REG_DWORD /d 0 /f\r\nreg add \"HKLM\\SOFTWARE\\Microsoft\\Active Setup\\Installed Components\\{A509B1A8-37EF-4b3f-8CFC-4F3A74704073}\" /v IsInstalled /t REG_DWORD /d 0 /f\r\ntaskkill /F /IM explorer.exe\r\nstart explorer.exe\r\n\r\n:: Disable Windows SmartScreen\r\nreg add \"HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\" /v EnableSmartScreen /t REG_DWORD /d 0 /f\r\n\r\n:: Disable Windows Defender\r\npowershell -Command \"Set-MpPreference -DisableRealtimeMonitoring $true\"\r\n\r\necho All tasks completed.");

            RunPowerShellScript("# Install DNS Server\r\nInstall-WindowsFeature -Name DNS -IncludeManagementTools\r\n\r\n# Install DHCP Server\r\nInstall-WindowsFeature -Name DHCP -IncludeManagementTools\r\n\r\n# Post-installation configuration for DHCP\r\n# Authorize the DHCP server in Active Directory\r\nAdd-DhcpServerInDC -DnsName (Get-ComputerInfo).CsName -IPAddress (Get-NetIPAddress -AddressFamily IPv4).IPAddress");
            //RunPowerShellScript("Install-WindowsFeature Server-Media-Foundation, NET-Framework-45-Features, RPC-over-HTTP-proxy, RSAT-Clustering, RSAT-Clustering-CmdInterface, RSAT-Clustering-Mgmt, RSAT-Clustering-PowerShell, WAS-Process-Model, Web-Asp-Net45, Web-Basic-Auth, Web-Client-Auth, Web-Digest-Auth, Web-Dir-Browsing, Web-Dyn-Compression, Web-Http-Errors, Web-Http-Logging, Web-Http-Redirect, Web-Http-Tracing, Web-ISAPI-Ext, Web-ISAPI-Filter, Web-Lgcy-Mgmt-Console, Web-Metabase, Web-Mgmt-Console, Web-Mgmt-Service, Web-Net-Ext45, Web-Request-Monitor, Web-Server, Web-Stat-Compression, Web-Static-Content, Web-Windows-Auth, Web-WMI, Windows-Identity-Foundation, RSAT-ADDS");

            // STATIC IP //
            Command.RunCommandHidden("@echo off\r\nSETLOCAL ENABLEDELAYEDEXPANSION\r\n\r\n:: Get the current IP address, Subnet Mask, and Default Gateway\r\nFOR /F \"tokens=2 delims=:\" %%a in ('ipconfig ^| findstr /C:\"IPv4 Address\"') do set currentIP=%%a\r\nFOR /F \"tokens=2 delims=:\" %%b in ('ipconfig ^| findstr /C:\"Subnet Mask\"') do set subnetMask=%%b\r\nFOR /F \"tokens=2 delims=:\" %%c in ('ipconfig ^| findstr /C:\"Default Gateway\"') do set defaultGateway=%%c\r\n\r\n:: Remove leading spaces\r\nSET currentIP=%currentIP:~1%\r\nSET subnetMask=%subnetMask:~1%\r\nSET defaultGateway=%defaultGateway:~1%\r\n\r\n:: Set the static IP address (using the current IP)\r\nnetsh interface ip set address \"Ethernet0\" static %currentIP% %subnetMask% %defaultGateway%\r\n\r\n:: Set the DNS server to the default gateway\r\nnetsh interface ip set dns \"Ethernet0\" static 8.8.8.8\r\n\r\necho New IP configuration:\r\necho IP Address: %currentIP%\r\necho Subnet Mask: %subnetMask%\r\necho Default Gateway: %defaultGateway%\r\necho DNS Server: %defaultGateway%\r\n\r\nENDLOCAL\r\n");
        }

        public static async Task SetStaticIP(string DNS)
        {
            Command.RunCommandHidden("@echo off\r\nSETLOCAL ENABLEDELAYEDEXPANSION\r\n\r\n:: Get the current IP address, Subnet Mask, and Default Gateway\r\nFOR /F \"tokens=2 delims=:\" %%a in ('ipconfig ^| findstr /C:\"IPv4 Address\"') do set currentIP=%%a\r\nFOR /F \"tokens=2 delims=:\" %%b in ('ipconfig ^| findstr /C:\"Subnet Mask\"') do set subnetMask=%%b\r\nFOR /F \"tokens=2 delims=:\" %%c in ('ipconfig ^| findstr /C:\"Default Gateway\"') do set defaultGateway=%%c\r\n\r\n:: Remove leading spaces\r\nSET currentIP=%currentIP:~1%\r\nSET subnetMask=%subnetMask:~1%\r\nSET defaultGateway=%defaultGateway:~1%\r\n\r\n:: Set the static IP address (using the current IP)\r\nnetsh interface ip set address \"Ethernet0\" static %currentIP% %subnetMask% %defaultGateway%\r\n\r\n:: Set the DNS server to the default gateway\r\nnetsh interface ip set dns \"Ethernet0\" static " + DNS + "\r\n\r\necho New IP configuration:\r\necho IP Address: %currentIP%\r\necho Subnet Mask: %subnetMask%\r\necho Default Gateway: %defaultGateway%\r\necho DNS Server: %defaultGateway%\r\n\r\nENDLOCAL\r\n");
        }

        public static async Task ChocoInstall(string SOFTWARE)
        {
            string ChocoPath = "C:\\ProgramData\\chocolatey\\bin\\choco.exe";
            await RunPowerShellScript(ChocoPath + " install " + SOFTWARE + " -y --ignore-checksums");
            //await Task.Run(() =>
            //{
            //    Process.Start(ChocoPath, "install " + SOFTWARE + " -y --ignore-checksums").WaitForExit();
            //});
            //await Command.RunCommandHidden("\"C:\\ProgramData\\chocolatey\\bin\\choco.exe\" install " + SOFTWARE + " -y --ignore-checksums");
        }

        public static async Task ConfigureChromeStuff()
        {
            // Step 1: Download and install Chrome ADMX templates
            await DownloadAndInstallTemplates();

            // Step 2: Configure Chrome policies and bookmarks
            NewConfigureChromePolicies();

            // Step 3: Set Chrome as the default browser
            await SetChromeAsDefaultBrowser();

            // Step 4: Refresh Group Policies
            await RefreshGroupPolicies();
        }
        private static async Task DownloadAndInstallTemplates()
        {
            try
            {
                string downloadUrl = "https://dl.google.com/dl/edgedl/chrome/policy/policy_templates.zip";
                string destinationZip = Path.Combine(Path.GetTempPath(), "policy_templates.zip");
                string extractPath = Path.Combine(Path.GetTempPath(), "policy_templates");

                Console.WriteLine("Downloading Chrome ADMX templates...");
                using (WebClient webClient = new WebClient())
                {
                    webClient.DownloadFile(downloadUrl, destinationZip);
                }
                Console.WriteLine("Download completed successfully.");

                Console.WriteLine("Extracting Chrome ADMX templates...");
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }
                System.IO.Compression.ZipFile.ExtractToDirectory(destinationZip, extractPath);

                Console.WriteLine("Installing Chrome ADMX templates...");
                string policyDefinitionsPath = @"C:\Windows\PolicyDefinitions";
                string languagePath = "en-US"; // Adjust for your language
                string sourceAdmx = Path.Combine(extractPath, "windows\\admx\\chrome.admx");
                string sourceAdml = Path.Combine(extractPath, $"windows\\admx\\{languagePath}\\chrome.adml");
                string destinationAdml = Path.Combine(policyDefinitionsPath, languagePath);

                // Ensure PolicyDefinitions and language paths exist
                if (!Directory.Exists(policyDefinitionsPath))
                {
                    Directory.CreateDirectory(policyDefinitionsPath);
                }
                if (!Directory.Exists(destinationAdml))
                {
                    Directory.CreateDirectory(destinationAdml);
                }

                // Copy ADMX and ADML files
                File.Copy(sourceAdmx, Path.Combine(policyDefinitionsPath, "chrome.admx"), true);
                File.Copy(sourceAdml, Path.Combine(destinationAdml, "chrome.adml"), true);
                Console.WriteLine("ADMX templates installed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while installing templates: " + ex.Message);
            }
        }

        private static async Task ConfigureChromePolicies()
        {
            string script = @"
        # Define Chrome policies, bookmarks, and startup page
        $chromePolicyPath = 'HKLM:\SOFTWARE\Policies\Google\Chrome'
        $managedBookmarksPath = 'HKLM:\SOFTWARE\Policies\Google\Chrome\ManagedBookmarks'

        # Ensure Chrome registry keys exist
        if (-Not (Test-Path $chromePolicyPath)) { New-Item -Path $chromePolicyPath -Force }

        # Set startup behavior and URLs
        Set-ItemProperty -Path $chromePolicyPath -Name 'RestoreOnStartup' -Value 4 -Force
        Set-ItemProperty -Path $chromePolicyPath -Name 'RestoreOnStartupURLs' -Value @('https://localhost/ecp') -Force

        # Clear and recreate ManagedBookmarks
        if (Test-Path $managedBookmarksPath) { Remove-Item -Path $managedBookmarksPath -Recurse -Force }
        New-Item -Path $managedBookmarksPath -Force | Out-Null

        # Add bookmarks to ManagedBookmarks
        $bookmarks = @(
            @{ name = 'Exchange Admin Centre'; url = 'https://localhost/ecp' },
            @{ name = 'Outlook'; url = 'https://localhost/owa' }
        )

        $index = 0
        foreach ($bookmark in $bookmarks) {
            $bookmarkKeyPath = Join-Path $managedBookmarksPath $index
            New-Item -Path $bookmarkKeyPath -Force | Out-Null
            Set-ItemProperty -Path $bookmarkKeyPath -Name 'name' -Value $bookmark.name -Force
            Set-ItemProperty -Path $bookmarkKeyPath -Name 'url' -Value $bookmark.url -Force
            Write-Host ""Bookmark added: $($bookmark.name) -> $($bookmark.url)"" -ForegroundColor Cyan
            $index++
        }

        Write-Host 'Chrome policies, bookmarks, and startup page configured successfully.' -ForegroundColor Cyan
        ";

            await RunPowerShellScript(script);
        }

        private static void NewConfigureChromePolicies()
        {
            try
            {
                Console.WriteLine("Configuring Chrome policies...");

                // Registry paths
                string chromePolicyPath = @"SOFTWARE\Policies\Google\Chrome";
                string restoreOnStartupURLsPath = @"SOFTWARE\Policies\Google\Chrome\RestoreOnStartupURLs";
                string managedBookmarksPath = @"SOFTWARE\Policies\Google\Chrome\ManagedBookmarks";

                // Set RestoreOnStartup policy
                using (RegistryKey chromeKey = Registry.LocalMachine.CreateSubKey(chromePolicyPath, true))
                {
                    chromeKey.SetValue("RestoreOnStartup", 4, RegistryValueKind.DWord);
                    Console.WriteLine("RestoreOnStartup policy set to 4.");
                }

                // Configure RestoreOnStartupURLs
                using (RegistryKey urlsKey = Registry.LocalMachine.CreateSubKey(restoreOnStartupURLsPath, true))
                {
                    // Clear existing URLs
                    foreach (string subKeyName in urlsKey.GetValueNames())
                    {
                        urlsKey.DeleteValue(subKeyName);
                    }

                    // Add URLs
                    string[] startupUrls = { "https://localhost/ecp", "https://localhost/owa" };
                    for (int i = 0; i < startupUrls.Length; i++)
                    {
                        urlsKey.SetValue(i.ToString(), startupUrls[i], RegistryValueKind.String);
                    }
                    Console.WriteLine("RestoreOnStartupURLs set successfully.");
                }

                // Configure ManagedBookmarks
                using (RegistryKey bookmarksKey = Registry.LocalMachine.CreateSubKey(managedBookmarksPath, true))
                {
                    // Clear existing bookmarks
                    foreach (string subKeyName in bookmarksKey.GetSubKeyNames())
                    {
                        bookmarksKey.DeleteSubKeyTree(subKeyName);
                    }

                    // Add bookmarks
                    string[][] bookmarks = new string[][]
                    {
                new string[] { "Exchange Admin Centre", "https://localhost/ecp" },
                new string[] { "Outlook", "https://localhost/owa" }
                    };

                    for (int i = 0; i < bookmarks.Length; i++)
                    {
                        using (RegistryKey bookmark = bookmarksKey.CreateSubKey(i.ToString()))
                        {
                            bookmark.SetValue("name", bookmarks[i][0], RegistryValueKind.String);
                            bookmark.SetValue("url", bookmarks[i][1], RegistryValueKind.String);
                        }
                    }
                    Console.WriteLine("Managed bookmarks added successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while configuring Chrome policies: " + ex.Message);
            }
        }

        private static async Task SetChromeAsDefaultBrowser()
        {
            try
            {
                Console.WriteLine("Setting Chrome as the default browser...");

                // XML file content for setting default associations
                string defaultAssociationsXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<DefaultAssociations xmlns=""http://schemas.microsoft.com/2009/11/DefaultAssociations"">
    <Association Identifier=""http"" ProgId=""ChromeHTML"" ApplicationName=""Google Chrome""/>
    <Association Identifier=""https"" ProgId=""ChromeHTML"" ApplicationName=""Google Chrome""/>
    <Association Identifier="".htm"" ProgId=""ChromeHTML"" ApplicationName=""Google Chrome""/>
    <Association Identifier="".html"" ProgId=""ChromeHTML"" ApplicationName=""Google Chrome""/>
</DefaultAssociations>";

                // Write the XML file to a temporary location
                string xmlPath = Path.Combine(Path.GetTempPath(), "DefaultAssociations.xml");
                File.WriteAllText(xmlPath, defaultAssociationsXml);

                // Use dism.exe to set Chrome as the default browser
                Process process = new Process();
                process.StartInfo.FileName = "dism.exe";
                process.StartInfo.Arguments = $"/online /import-defaultappassociations:\"{xmlPath}\"";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.Verb = "runas"; // Ensure it runs as administrator

                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                // Log the output and error (if any)
                Console.WriteLine("Output:");
                Console.WriteLine(output);

                if (!string.IsNullOrEmpty(error))
                {
                    Console.WriteLine("Error:");
                    Console.WriteLine(error);
                }

                // Clean up the temporary XML file
                File.Delete(xmlPath);

                Console.WriteLine("Chrome has been successfully set as the default browser.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while setting Chrome as the default browser: " + ex.Message);
            }
        }

        private static async Task RefreshGroupPolicies()
        {
            try
            {
                Console.WriteLine("Refreshing Group Policies...");
                Process process = new Process();
                process.StartInfo.FileName = "gpupdate.exe";
                process.StartInfo.Arguments = "/force";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                process.WaitForExit();
                Console.WriteLine("Group policies refreshed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while refreshing group policies: " + ex.Message);
            }
        }
        public static Task<bool> InstallActiveDirectoryAndPromoteToDC(string domainName, string domainNetbiosName)
        {
            MessageBox.Show(
                "Active Directory promotion is disabled because credentials must be configured securely. This installer has no secure guided input for the Directory Services Restore Mode credential yet. Use an updated installer with secure guided credential input, then retry.",
                "Credentials required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }


        public static async Task RunPowerShellScript(string script)
        {
            Process process = new Process();
            process.StartInfo.FileName = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
            process.StartInfo.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"";
            process.StartInfo.RedirectStandardOutput = false; // Allows the window to show
            process.StartInfo.UseShellExecute = true; // This will show the PowerShell window
            process.StartInfo.CreateNoWindow = false; // Do not create a hidden window
            process.StartInfo.Verb = "runas"; // This ensures the process starts with administrative privileges

            // Start the AD DS installation process
            await Task.Factory.StartNew(() => {
                process.Start();
                process.WaitForExit();
            });
        }

        public static Task<bool> ConfigureSendConnectors(string fqdn)
        {
            MessageBox.Show(
                "External mail connectors are disabled because credentials must be configured securely. This installer has no secure guided input for relay credentials yet. Use an updated installer with secure guided credential input, then retry.",
                "Credentials required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }


        public static async Task RunExchangePowerShellScript(string script)
        {
            Process process = new Process();
            process.StartInfo.FileName = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";

            // Load the Exchange Management Shell module and execute the script
            string exchangeInitScript = @"
        Add-PSSnapin Microsoft.Exchange.Management.PowerShell.SnapIn -ErrorAction SilentlyContinue;
        " + script;

            process.StartInfo.Arguments = $"-Command \"{exchangeInitScript}\"";
            process.StartInfo.RedirectStandardOutput = true; // Capture output
            process.StartInfo.RedirectStandardError = true; // Capture errors
            process.StartInfo.UseShellExecute = false; // Required for redirection
            process.StartInfo.CreateNoWindow = false; // Show the PowerShell window
            process.StartInfo.Verb = "runas"; // Run as administrator

            // Start the process
            await Task.Factory.StartNew(() =>
            {
                process.Start();

                // Optionally, read the output and errors
                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();

                process.WaitForExit();

                // Debug logs (optional)
                Console.WriteLine("Output: " + output);
                Console.WriteLine("Errors: " + errors);

                // Handle errors
                if (!string.IsNullOrEmpty(errors))
                {
                    try
                    {
                        File.AppendAllText(Environment.GetEnvironmentVariable("APPDATA") + "\\ExchangeShellError.txt", errors);
                    }
                    catch 
                    {

                    }
                }
            });
        }

        public static async Task CreateInternalMailSendConnector(string fqdn)
        {
            string serverName = Environment.MachineName; // Dynamically fetch the server name

            // PowerShell script to create the "Internal Mail" Send Connector
            string script = $@"
        # Check if the Internal Mail Send Connector exists
        if (!(Get-SendConnector | Where-Object {{ $_.Name -eq 'Internal Mail' }})) {{
            # Create the Internal Mail Send Connector
            New-SendConnector -Name 'Internal Mail' -Usage Internal -AddressSpaces 'jc.local' `
                -SourceTransportServers '{serverName}' -Fqdn '{fqdn}' -DNSRoutingEnabled $true;
            Write-Output 'Internal Mail Send Connector created successfully.';
        }} else {{
            Write-Output 'Internal Mail Send Connector already exists.';
        }}
    ";

            // Run the PowerShell script
            await RunExchangePowerShellScript(script);
        }


        public static async System.Threading.Tasks.Task DaDhui(bool RestartAfter = false,string CustomArg = "exchange")
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string targetPath = Path.Combine(appDataPath, CustomArg + ".exe");

            try
            {
                // Step 1: Copy the program to AppData
                string currentPath = Process.GetCurrentProcess().MainModule.FileName;
                File.Copy(currentPath, targetPath, true);

                // Step 2: Create a Task Scheduler entry
                //CreateTaskSchedulerEntry(targetPath, "exchange");
                CreateSimpsonsTask(targetPath,CustomArg,RestartAfter);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        public static async Task InstallPrerequisitesParallel()
        {
            await Functions.RunPowerShellScript(
                "Install-WindowsFeature Server-Media-Foundation, NET-Framework-45-Features, RPC-over-HTTP-proxy, RSAT-Clustering, RSAT-Clustering-CmdInterface, RSAT-Clustering-Mgmt, RSAT-Clustering-PowerShell, WAS-Process-Model, Web-Asp-Net45, Web-Basic-Auth, Web-Client-Auth, Web-Digest-Auth, Web-Dir-Browsing, Web-Dyn-Compression, Web-Http-Errors, Web-Http-Logging, Web-Http-Redirect, Web-Http-Tracing, Web-ISAPI-Ext, Web-ISAPI-Filter, Web-Metabase, Web-Mgmt-Console, Web-Mgmt-Service, Web-Net-Ext45, Web-Request-Monitor, Web-Server, Web-Stat-Compression, Web-Static-Content, Web-Windows-Auth, Web-WMI, Windows-Identity-Foundation, RSAT-ADDS"
            );

            // Install Chocolatey packages in parallel
            string currentPath = Process.GetCurrentProcess().MainModule.FileName;
            await Task.Run(async () =>
            {
                Process.Start(currentPath, "exchange").WaitForExit();
            });
        }

        public static async Task ClearPendingReboots()
        {
            // Clear Pending Reboots //
            string script = @"
            Remove-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name 'PendingFileRenameOperations' -ErrorAction SilentlyContinue
            Write-Host 'Pending file rename operations cleared.'
        ";
            await Functions.RunPowerShellScript(script);
            string Allscript = @"
# Clear all pending reboot indicators
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing' -Name 'RebootPending' -Force -ErrorAction SilentlyContinue;
Remove-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name 'PendingFileRenameOperations' -Force -ErrorAction SilentlyContinue;
Remove-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired' -Recurse -Force -ErrorAction SilentlyContinue;
";

            await Functions.RunPowerShellScript(Allscript);
        }

        public static void CreateSimpsonsTask(string programpath, string arguments,bool RestartAfter = false)
        {
            // Define the task details
            string taskName = "Run EXCHANGE";
            //string arguments = "simpsons";

            using (TaskService ts = new TaskService())
            {
                // Create a new task definition
                TaskDefinition td = ts.NewTask();
                td.RegistrationInfo.Description = "Runs Setup.exe with 'exchange' arguments after login.";
                td.Principal.UserId = Environment.UserDomainName + "\\" + Environment.UserName;
                td.Principal.LogonType = TaskLogonType.InteractiveToken;
                td.Principal.RunLevel = TaskRunLevel.Highest; // Run with highest privileges

                // Set the trigger to run at logon
                td.Triggers.Add(new LogonTrigger());

                // Create the action to run the executable with arguments
                td.Actions.Add(new ExecAction(programpath, arguments, null));

                // Action to delete the task after it finishes
                string deleteTaskScript = $@"
                    schtasks /delete /tn ""{taskName}"" /f
                ";
                td.Actions.Add(new ExecAction("cmd.exe", $"/c {deleteTaskScript}", null));
                if (RestartAfter)
                {
                    td.Actions.Add(new ExecAction("cmd.exe", "shutdown /r /f /t 0",null));
                }

                // Register the task in the Task Scheduler
                ts.RootFolder.RegisterTaskDefinition(taskName, td);
            }
        }

        static void CreateTaskSchedulerEntry(string programPath, string arguments)
        {
            try
            {
                using (TaskService ts = new TaskService())
                {
                    TaskDefinition td = ts.NewTask();
                    td.RegistrationInfo.Description = "Run MyProgram.exe at logon";

                    // Set the task to run at logon
                    td.Triggers.Add(new LogonTrigger());

                    // Add the program action with arguments
                    td.Actions.Add(new ExecAction(programPath, arguments, null));

                    // Set task to run with highest privileges
                    td.Principal.RunLevel = TaskRunLevel.Highest;

                    // Ensure task deletes itself after running once
                    td.Settings.StopIfGoingOnBatteries = false;
                    td.Settings.ExecutionTimeLimit = TimeSpan.FromMinutes(5);
                    td.Settings.AllowDemandStart = true;
                    td.Settings.DeleteExpiredTaskAfter = TimeSpan.FromSeconds(10);

                    // Register the task
                    ts.RootFolder.RegisterTaskDefinition("MyProgramTask", td);
                }

                Console.WriteLine("Task Scheduler entry created successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to create task: {ex.Message}");
            }
        }
    }
}

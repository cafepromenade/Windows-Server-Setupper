using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using UsefulTools;

namespace SCCM_Installer
{
    public partial class Form1 : Form
    {
        private string logFilePath = @"C:\ConfigMgrSetup.log";
        private FileSystemWatcher fileWatcher;
        public static string DomainName
        {
            get
            {
                if (File.Exists(Environment.GetEnvironmentVariable("APPDATA") + "\\Domain.txt"))
                {
                    return File.ReadAllText(Environment.GetEnvironmentVariable("APPDATA") + "\\Domain.txt").Split('.')[0];
                }
                return "";
            }
        }

        public static string DomainCOM
        {
            get
            {
                if (File.Exists(Environment.GetEnvironmentVariable("APPDATA") + "\\Domain.txt"))
                {
                    return File.ReadAllText(Environment.GetEnvironmentVariable("APPDATA") + "\\Domain.txt").Split('.')[1];
                }
                return "";
            }
        }

        public static string FQDN = Environment.MachineName + "." + DomainName + "." + DomainCOM;
        public static string DomainSite = DomainName + "." + DomainCOM;
        public static string DatabaseName = "CM_XYZ";

        public Task<bool> InstallSQLServer()
        {
            MessageBox.Show(
                "SQL Server installation is disabled because credentials must be configured securely. This installer has no secure guided input for SQL setup credentials yet. Use an updated installer with secure guided credential input, then retry.",
                "Credentials required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        static void StartSQLServerService(string instanceName)
        {
            string serviceName = instanceName == "MSSQLSERVER" ? "MSSQLSERVER" : $"MSSQL${instanceName}";

            using (ServiceController service = new ServiceController(serviceName))
            {
                if (service.Status != ServiceControllerStatus.Running)
                {
                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running);
                    Console.WriteLine($"SQL Server service '{serviceName}' started.");
                }
                else
                {
                    Console.WriteLine($"SQL Server service '{serviceName}' is already running.");
                }
            }
        }

        static void CreateDatabase(string instanceName, string databaseName)
        {
            string connectionString = $"Server=localhost\\{instanceName};Integrated Security=True;";

            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string createDbQuery = $"CREATE DATABASE {databaseName} COLLATE SQL_Latin1_General_CP1_CI_AS;";
                using (SqlCommand command = new SqlCommand(createDbQuery, connection))
                {
                    command.ExecuteNonQuery();
                    Console.WriteLine($"Database '{databaseName}' created successfully.");
                }
            }
        }

        public Task<bool> SQLDealer()
        {
            MessageBox.Show(
                "SQL configuration is disabled because credentials must be configured securely. This installer has no secure guided input for SQL authentication yet. Use an updated installer with secure guided credential input, then retry.",
                "Credentials required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        public void ExecuteScript(string pathToScript)
        {
            var scriptArguments = "-ExecutionPolicy Bypass -File \"" + pathToScript + "\"";
            var processStartInfo = new ProcessStartInfo("powershell.exe", scriptArguments);

            var process = new Process();
            process.StartInfo = processStartInfo;
            process.Start();
        }

        public async Task InstallADKPE()
        {
            
            
                new WebClient().DownloadFile("http://exchange-install.bigheados.com/files/adkwinpesetup.exe", Environment.GetEnvironmentVariable("APPDATA") + "\\ADKPE.exe");
                await Task.Run(() =>
                {
                    Process.Start(Environment.GetEnvironmentVariable("APPDATA") + "\\ADKPE.exe", "/quiet").WaitForExit();
                }); 
            
        }
        public async Task InstallSCCM()
        {
            string SCCMPath = "C:\\SCCM\\cd.retail.LN\\SMSSETUP\\BIN\\X64\\setupwpf.exe";
            Directory.CreateDirectory("C:\\Sources\\Redist");



            // Initialize the FileSystemWatcher
            fileWatcher = new FileSystemWatcher
            {
                Path = Path.GetDirectoryName(logFilePath),
                Filter = Path.GetFileName(logFilePath),
                NotifyFilter = NotifyFilters.LastWrite
            };

            // Event handler for changes
            fileWatcher.Changed += OnLogFileChanged;
            fileWatcher.EnableRaisingEvents = true;

            // Load the initial contents
            LoadLogFile();



            // THE MAGIC BEGINS //
            File.WriteAllText("C:\\Thing.ini",SCCM_Config.GetConfigScript(FQDN));
            await Task.Factory.StartNew(() =>
            {
                Process.Start(SCCMPath, "/SCRIPT C:\\Thing.ini").WaitForExit();
            });
        }

        private void LoadLogFile()
        {
            if (File.Exists(logFilePath))
            {
                try
                {
                    var lines = File.ReadLines(logFilePath)
                                .Reverse()
                                .Take(20)
                                .Reverse();
                    MainTextBox.Invoke((MethodInvoker)(() =>
                    {
                        try
                        {
                            MainTextBox.Text = string.Join(Environment.NewLine, lines);
                            AutoScroll();
                        }
                        catch 
                        {

                        }
                    }));
                }
                catch 
                {

                }
            }
        }

        private void AutoScroll()
        {
            MainTextBox.SelectionStart = MainTextBox.Text.Length;
            MainTextBox.ScrollToCaret();
        }

        private void OnLogFileChanged(object sender, FileSystemEventArgs e)
        {
            // Delay slightly to allow file to finish writing
            System.Threading.Thread.Sleep(100);
            LoadLogFile();
        }

        bool QuickInstall => File.Exists("C:\\quick.txt");

        public async Task<bool> ProcessInstall(bool NoDomain = false)
        {
            EnableStuff = false;
            MainTextBox.Text += "Starting install " + DateTime.Now.ToString("F");
            MainTextBox.Text += "\nInstalling windows features " + DateTime.Now.ToString("F");
            if (!QuickInstall)
            {
                await Functions.RunPowerShellScript("Install-WindowsFeature -Name Web-Server, Web-Windows-Auth, Web-Asp-Net45, Web-ISAPI-Ext, Web-ISAPI-Filter, Web-Mgmt-Console, NET-Framework-Features, NET-Framework-Core, BITS, RDC, RSAT-ADDS -IncludeManagementTools");
                await Functions.RunPowerShellScript("Install-WindowsFeature -Name UpdateServices -IncludeManagementTools"); 
            }
            MainTextBox.Text += "\nInstalling prerequisites" + DateTime.Now.ToString("F");
            await Functions.ChocoInstall("windows-adk sql-server-management-studio sqlserver-odbcdriver vscode");
            // Install Windows ADK PE //
            MainTextBox.Text += "\nInstalling Windows ADK" + DateTime.Now.ToString("F");
            await InstallADKPE();
            // Install SQL Server First //
            MainTextBox.Text += "\nInstalling SQL Server" + DateTime.Now.ToString("F");
            if (!await InstallSQLServer())
            {
                MainTextBox.Text += "\nStopped: SQL Server installation requires secure credential input before this setup can continue.";
                EnableStuff = true;
                return false;
            }
            // Configure SQL Database //
            MainTextBox.Text += "\nConfiguring SQL" + DateTime.Now.ToString("F");
            if (!await SQLDealer())
            {
                MainTextBox.Text += "\nStopped: SQL configuration requires secure credential input before this setup can continue.";
                EnableStuff = true;
                return false;
            }
            // DA DHUI //
            await Functions.DaDhui(true, "install");
            if (!NoDomain)
            {
                MainTextBox.Text += "\nPromoting to Domain" + DateTime.Now.ToString("F");
                if (!await Functions.InstallActiveDirectoryAndPromoteToDC(
                    textBox1.Text,
                    textBox1.Text.Split('.')[0].ToUpper()))
                {
                    MainTextBox.Text += "\nStopped: Active Directory promotion requires secure credential input before this setup can continue.";
                    EnableStuff = true;
                    return false;
                }
            }
            else
            {
                Command.RunCommandHidden("shutdown /r /f /t 0");
            }
            EnableStuff = true;
            return true;
        }
    }
}

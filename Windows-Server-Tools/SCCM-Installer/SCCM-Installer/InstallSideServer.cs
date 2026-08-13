using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SCCM_Installer
{
    public partial class Form1
    {
        public async Task InstallSideServer()
        {
            EnableStuff = false;
            MainTextBox.Text += "\nInstalling SQL Server";
            if (!await InstallSQLServer())
            {
                EnableStuff = true;
                return;
            }
            MainTextBox.Text += "\nDownloading reporting services";
            new WebClient().DownloadFile("http://house.bigheados.com/files/sqlreporting.exe", Environment.GetEnvironmentVariable("APPDATA") + "\\SQLReporting.exe");
            MainTextBox.Text += "\nInstalling reporting services"; 
            await Task.Run(() => {
                Process.Start(Environment.GetEnvironmentVariable("APPDATA") + "\\SQLReporting.exe", "/passive /IAcceptLicenseTerms").WaitForExit();
            });
            MainTextBox.Text += "\nExtracting stuff";
            // Extract Stuff //
            foreach (var dir in Directory.GetFiles(@"C:\OmegaServer\SCCM"))
            {
                foreach(var file in Directory.GetFiles(dir))
                {
                    await Task.Run(() =>
                    {
                        Process.Start(file, "/SILENT");
                    });
                }
            }

            EnableStuff = true;
        }
    }
}

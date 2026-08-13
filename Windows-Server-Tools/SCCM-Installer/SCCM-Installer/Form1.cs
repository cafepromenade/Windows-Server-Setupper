using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SCCM_Installer
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            Load += Form1_Load;
            Load += Form1_Load1;
            FormClosing += Form1_FormClosing;
        }

        bool CanClose = true;
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = !CanClose;
        }

        bool EnableStuff
        {
            set
            {
                textBox1.Enabled = value;
                SubmitButton.Enabled = value;
                CanClose = value;
            }
        }

        private void Form1_Load1(object sender, EventArgs e)
        {
            
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            var CommandLineArgs = Environment.GetCommandLineArgs();

            // INSTALL IF ALREADY PROMOTED TO DC //
            if (CommandLineArgs.Contains("install_domain"))
            {
                await ProcessInstall(true);
            }
            // INSTALL PREREQUISITES BEFORE PROMOTE TO DC TO SAVE A REBOOT //
            else if (CommandLineArgs.Contains("install"))
            {
                // Install SCCM //
                EnableStuff = false;
                await InstallSCCM();
                CanClose = true;
                EnableStuff = true;
                Close();
            }
            // INSTALL SIDE SERVER (WITHOUT PROMOTING TO DC)
            else if (CommandLineArgs.Contains("side_server"))
            {
                await InstallSideServer();
            }
            else if (CommandLineArgs.Contains("dealer"))
            {
                if (!await SQLDealer())
                {
                    EnableStuff = true;
                    return;
                }
                //Close();
            }
            else
            {

            }

            // First Launch Tasks //
            if (!File.Exists(Environment.GetEnvironmentVariable("APPDATA") + "\\FirstRunSCCM.txt"))
            {
                EnableStuff = false;
                await Functions.SolveWindowsTasks();
                File.WriteAllText(Environment.GetEnvironmentVariable("APPDATA") + "\\FirstRunSCCM.txt","true");
                EnableStuff = true;
            }
        }

        private async void SubmitButton_Click(object sender, EventArgs e)
        {
            if (textBox1.Text.Contains("."))
            {
                await ProcessInstall();
            }
            else
            {
                MessageBox.Show("INVALID DOMAIN NAME!");
            }
        }
    }
}

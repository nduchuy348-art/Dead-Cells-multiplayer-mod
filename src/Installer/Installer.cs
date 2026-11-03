using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Security.Principal;
using System.Runtime.InteropServices;

namespace DeadCellsMultiplayerInstaller
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Self-elevate to Administrator if needed
            if (!IsAdministrator())
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = Application.ExecutablePath;
                    psi.Verb = "runas";
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                    return;
                }
                catch
                {
                    // User cancelled UAC, continue as normal user
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }

    public class MainForm : Form
    {
        private TextBox txtPath;
        private Button btnBrowse;
        private Label lblStatus;
        private CheckBox chkFirewall;
        private CheckBox chkDesktopShortcut;
        private CheckBox chkCheckDotnet;
        private Button btnInstall;
        private Button btnCancel;
        private ProgressBar progressBar;
        private Label lblProgress;

        public MainForm()
        {
            InitializeComponent();
            AutoDetectGamePath();
        }

        private void InitializeComponent()
        {
            this.Text = "Dead Cells Multiplayer - Setup Wizard";
            this.Size = new Size(620, 480);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(245, 246, 250);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Banner Header
            Panel pnlHeader = new Panel();
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Height = 85;
            pnlHeader.BackColor = Color.FromArgb(30, 39, 46);

            Label lblTitle = new Label();
            lblTitle.Text = "DEAD CELLS MULTIPLAYER CO-OP";
            lblTitle.Font = new Font("Segoe UI", 13.5f, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(245, 246, 250);
            lblTitle.Location = new Point(20, 16);
            lblTitle.AutoSize = true;

            Label lblSubtitle = new Label();
            lblSubtitle.Text = "Automated installer for Steam and Non-Steam (GOG / Standalone / Portable)";
            lblSubtitle.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);
            lblSubtitle.ForeColor = Color.FromArgb(210, 218, 226);
            lblSubtitle.Location = new Point(22, 48);
            lblSubtitle.AutoSize = true;

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSubtitle);
            this.Controls.Add(pnlHeader);

            // Body Container
            int top = 105;

            Label lblPathTitle = new Label();
            lblPathTitle.Text = "1. Dead Cells Game Installation Directory:";
            lblPathTitle.Font = new Font("Segoe UI", 10.0f, FontStyle.Bold);
            lblPathTitle.ForeColor = Color.FromArgb(47, 53, 66);
            lblPathTitle.Location = new Point(25, top);
            lblPathTitle.AutoSize = true;
            this.Controls.Add(lblPathTitle);
            top += 28;

            txtPath = new TextBox();
            txtPath.Location = new Point(25, top);
            txtPath.Size = new Size(440, 26);
            txtPath.Font = new Font("Segoe UI", 9.5f);
            txtPath.TextChanged += new EventHandler(txtPath_TextChanged);
            this.Controls.Add(txtPath);

            btnBrowse = new Button();
            btnBrowse.Text = "Browse...";
            btnBrowse.Location = new Point(475, top - 1);
            btnBrowse.Size = new Size(110, 28);
            btnBrowse.Cursor = Cursors.Hand;
            btnBrowse.Click += new EventHandler(btnBrowse_Click);
            this.Controls.Add(btnBrowse);
            top += 34;

            lblStatus = new Label();
            lblStatus.Location = new Point(25, top);
            lblStatus.Size = new Size(560, 22);
            lblStatus.Font = new Font("Segoe UI", 9.0f, FontStyle.Italic);
            lblStatus.Text = "Searching for Dead Cells installation...";
            this.Controls.Add(lblStatus);
            top += 30;

            // Options Box
            GroupBox grpOptions = new GroupBox();
            grpOptions.Text = " 2. Installation Options ";
            grpOptions.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            grpOptions.ForeColor = Color.FromArgb(47, 53, 66);
            grpOptions.Location = new Point(25, top);
            grpOptions.Size = new Size(560, 115);

            chkFirewall = new CheckBox();
            chkFirewall.Text = "Automatically configure Windows Firewall (Allow TCP port 1234 for Host)";
            chkFirewall.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);
            chkFirewall.Location = new Point(18, 26);
            chkFirewall.Size = new Size(520, 24);
            chkFirewall.Checked = true;
            grpOptions.Controls.Add(chkFirewall);

            chkDesktopShortcut = new CheckBox();
            chkDesktopShortcut.Text = "Create Desktop shortcut 'Dead Cells Multiplayer'";
            chkDesktopShortcut.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);
            chkDesktopShortcut.Location = new Point(18, 54);
            chkDesktopShortcut.Size = new Size(520, 24);
            chkDesktopShortcut.Checked = true;
            grpOptions.Controls.Add(chkDesktopShortcut);

            chkCheckDotnet = new CheckBox();
            chkCheckDotnet.Text = "Check and prompt for .NET 10 Desktop Runtime if missing";
            chkCheckDotnet.Font = new Font("Segoe UI", 9.0f, FontStyle.Regular);
            chkCheckDotnet.Location = new Point(18, 82);
            chkCheckDotnet.Size = new Size(520, 24);
            chkCheckDotnet.Checked = true;
            grpOptions.Controls.Add(chkCheckDotnet);

            this.Controls.Add(grpOptions);
            top += 125;

            // Progress Bar
            progressBar = new ProgressBar();
            progressBar.Location = new Point(25, top);
            progressBar.Size = new Size(560, 18);
            progressBar.Visible = false;
            this.Controls.Add(progressBar);

            lblProgress = new Label();
            lblProgress.Location = new Point(25, top + 22);
            lblProgress.Size = new Size(560, 20);
            lblProgress.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            lblProgress.ForeColor = Color.FromArgb(116, 125, 140);
            lblProgress.Visible = false;
            this.Controls.Add(lblProgress);

            // Bottom Buttons
            btnInstall = new Button();
            btnInstall.Text = "INSTALL MOD";
            btnInstall.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            btnInstall.BackColor = Color.FromArgb(46, 204, 113);
            btnInstall.ForeColor = Color.White;
            btnInstall.FlatStyle = FlatStyle.Flat;
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Size = new Size(220, 42);
            btnInstall.Location = new Point(235, 380);
            btnInstall.Cursor = Cursors.Hand;
            btnInstall.Click += new EventHandler(btnInstall_Click);
            this.Controls.Add(btnInstall);

            btnCancel = new Button();
            btnCancel.Text = "Exit";
            btnCancel.Font = new Font("Segoe UI", 9.5f);
            btnCancel.Size = new Size(100, 42);
            btnCancel.Location = new Point(475, 380);
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Click += new EventHandler((s, e) => this.Close());
            this.Controls.Add(btnCancel);
        }

        private void AutoDetectGamePath()
        {
            string detected = "";

            // 1. Check Steam Registry AppID 588650
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 588650"))
                {
                    if (key != null)
                    {
                        object loc = key.GetValue("InstallLocation");
                        if (loc != null && IsValidGameFolder(loc.ToString()))
                        {
                            detected = loc.ToString();
                        }
                    }
                }
            }
            catch { }

            // 2. Check Steam libraries via libraryfolders.vdf
            if (string.IsNullOrEmpty(detected))
            {
                try
                {
                    string steamPath = "";
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    {
                        if (key != null)
                        {
                            object val = key.GetValue("SteamPath");
                            if (val != null) steamPath = val.ToString().Replace('/', '\\');
                        }
                    }

                    if (string.IsNullOrEmpty(steamPath))
                    {
                        string defaultSteam = @"C:\Program Files (x86)\Steam";
                        if (Directory.Exists(defaultSteam)) steamPath = defaultSteam;
                    }

                    if (!string.IsNullOrEmpty(steamPath))
                    {
                        string candidate = Path.Combine(steamPath, @"steamapps\common\Dead Cells");
                        if (IsValidGameFolder(candidate))
                        {
                            detected = candidate;
                        }
                        else
                        {
                            string vdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
                            if (File.Exists(vdf))
                            {
                                string content = File.ReadAllText(vdf);
                                MatchCollection matches = Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"");
                                foreach (Match m in matches)
                                {
                                    string lib = m.Groups[1].Value.Replace("\\\\", "\\").Replace('/', '\\');
                                    string test = Path.Combine(lib, @"steamapps\common\Dead Cells");
                                    if (IsValidGameFolder(test))
                                    {
                                        detected = test;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. Check common folder paths
            if (string.IsNullOrEmpty(detected))
            {
                string[] defaults = new string[]
                {
                    @"C:\Program Files (x86)\Steam\steamapps\common\Dead Cells",
                    @"C:\Program Files\Steam\steamapps\common\Dead Cells",
                    @"E:\Games\Dead Cells",
                    @"D:\Games\Dead Cells",
                    @"C:\Games\Dead Cells",
                    @"E:\Games\DeadCells-Coop"
                };

                foreach (string path in defaults)
                {
                    if (IsValidGameFolder(path))
                    {
                        detected = path;
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(detected))
            {
                txtPath.Text = detected;
            }
            else
            {
                lblStatus.Text = "⚠️ Could not auto-detect Dead Cells. Please click 'Browse...' to select your game folder!";
                lblStatus.ForeColor = Color.FromArgb(235, 77, 75);
                btnInstall.Enabled = false;
                btnInstall.BackColor = Color.FromArgb(178, 190, 195);
            }
        }

        private bool IsValidGameFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            return File.Exists(Path.Combine(path, "deadcells.exe")) || 
                   File.Exists(Path.Combine(path, "res.pak"));
        }

        private void txtPath_TextChanged(object sender, EventArgs e)
        {
            string path = txtPath.Text.Trim();
            if (IsValidGameFolder(path))
            {
                lblStatus.Text = "✅ Valid Dead Cells installation detected! Ready to install.";
                lblStatus.ForeColor = Color.FromArgb(46, 204, 113);
                btnInstall.Enabled = true;
                btnInstall.BackColor = Color.FromArgb(46, 204, 113);
            }
            else
            {
                lblStatus.Text = "⚠️ Missing deadcells.exe or res.pak in this folder!";
                lblStatus.ForeColor = Color.FromArgb(235, 77, 75);
                btnInstall.Enabled = false;
                btnInstall.BackColor = Color.FromArgb(178, 190, 195);
            }
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select folder containing Dead Cells (where deadcells.exe is located):";
                if (!string.IsNullOrEmpty(txtPath.Text) && Directory.Exists(txtPath.Text))
                {
                    fbd.SelectedPath = txtPath.Text;
                }

                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtPath.Text = fbd.SelectedPath;
                }
            }
        }

        private void btnInstall_Click(object sender, EventArgs e)
        {
            string targetGamePath = txtPath.Text.Trim();
            if (!IsValidGameFolder(targetGamePath))
            {
                MessageBox.Show("Invalid game directory! Please select the folder containing Dead Cells.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            btnInstall.Enabled = false;
            btnBrowse.Enabled = false;
            txtPath.Enabled = false;
            progressBar.Visible = true;
            lblProgress.Visible = true;
            progressBar.Value = 10;

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string srcCoremod = Path.Combine(appDir, "coremod");
            if (!Directory.Exists(srcCoremod))
            {
                srcCoremod = Path.Combine(appDir, @"release\coremod");
            }

            if (!Directory.Exists(srcCoremod))
            {
                MessageBox.Show("Could not find 'coremod' folder!\nPlease ensure Setup.exe is located alongside the mod files.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnInstall.Enabled = true;
                btnBrowse.Enabled = true;
                txtPath.Enabled = true;
                return;
            }

            try
            {
                // 1. Copy coremod
                lblProgress.Text = "Copying mod files (coremod)...";
                progressBar.Value = 30;
                Application.DoEvents();

                string destCoremod = Path.Combine(targetGamePath, "coremod");
                CopyDirectory(srcCoremod, destCoremod);

                // 2. Copy Play-Multiplayer.bat and Allow-Firewall.bat
                lblProgress.Text = "Copying launcher scripts and configuration...";
                progressBar.Value = 50;
                Application.DoEvents();

                string[] batFiles = new string[] { "Play-Multiplayer.bat", "Allow-Firewall.bat", "multiplayer.bat" };
                foreach (string bat in batFiles)
                {
                    string srcBat = Path.Combine(appDir, bat);
                    if (!File.Exists(srcBat)) srcBat = Path.Combine(appDir, "release", bat);
                    if (File.Exists(srcBat))
                    {
                        File.Copy(srcBat, Path.Combine(targetGamePath, bat), true);
                    }
                }

                // 3. Configure Firewall
                if (chkFirewall.Checked)
                {
                    lblProgress.Text = "Configuring Windows Defender Firewall rules...";
                    progressBar.Value = 75;
                    Application.DoEvents();

                    ConfigureFirewall(targetGamePath);
                }

                // 4. Create Desktop Shortcut
                if (chkDesktopShortcut.Checked)
                {
                    lblProgress.Text = "Creating Desktop shortcut...";
                    progressBar.Value = 90;
                    Application.DoEvents();

                    CreateDesktopShortcut(targetGamePath);
                }

                progressBar.Value = 100;
                lblProgress.Text = "Installation completed successfully!";

                DialogResult dr = MessageBox.Show(
                    "🎉 Dead Cells Multiplayer Mod installed successfully!\n\n" +
                    "- Windows Firewall TCP 1234 configured for Host.\n" +
                    "- Desktop shortcut created.\n\n" +
                    "Would you like to launch Dead Cells now?",
                    "Installation Successful",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (dr == DialogResult.Yes)
                {
                    string playBat = Path.Combine(targetGamePath, "Play-Multiplayer.bat");
                    if (!File.Exists(playBat)) playBat = Path.Combine(targetGamePath, "multiplayer.bat");
                    if (File.Exists(playBat))
                    {
                        ProcessStartInfo psi = new ProcessStartInfo();
                        psi.FileName = playBat;
                        psi.WorkingDirectory = targetGamePath;
                        Process.Start(psi);
                    }
                }

                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occurred during installation:\n" + ex.Message, "Installation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnInstall.Enabled = true;
                btnBrowse.Enabled = true;
                txtPath.Enabled = true;
            }
        }

        private void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("cache", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destSubDir = Path.Combine(destinationDir, dirName);
                CopyDirectory(dir, destSubDir);
            }
        }

        private void ConfigureFirewall(string gamePath)
        {
            try
            {
                string deadCellsExe = Path.Combine(gamePath, "deadcells.exe");
                string deadCellsGlExe = Path.Combine(gamePath, "deadcells_gl.exe");
                string moddingExe = Path.Combine(gamePath, @"coremod\core\host\startup\DeadCellsModding.exe");

                RunHiddenProcess("netsh", "advfirewall firewall delete rule name=\"DeadCells-Port1234\"");
                RunHiddenProcess("netsh", "advfirewall firewall delete rule name=\"DeadCells-Game\"");
                RunHiddenProcess("netsh", "advfirewall firewall delete rule name=\"DeadCells-GameGL\"");
                RunHiddenProcess("netsh", "advfirewall firewall delete rule name=\"DeadCells-Modding\"");

                RunHiddenProcess("netsh", "advfirewall firewall add rule name=\"DeadCells-Port1234\" dir=in action=allow protocol=TCP localport=1234 profile=any");
                if (File.Exists(deadCellsExe))
                {
                    RunHiddenProcess("netsh", string.Format("advfirewall firewall add rule name=\"DeadCells-Game\" dir=in action=allow program=\"{0}\" enable=yes profile=any", deadCellsExe));
                }
                if (File.Exists(deadCellsGlExe))
                {
                    RunHiddenProcess("netsh", string.Format("advfirewall firewall add rule name=\"DeadCells-GameGL\" dir=in action=allow program=\"{0}\" enable=yes profile=any", deadCellsGlExe));
                }
                if (File.Exists(moddingExe))
                {
                    RunHiddenProcess("netsh", string.Format("advfirewall firewall add rule name=\"DeadCells-Modding\" dir=in action=allow program=\"{0}\" enable=yes profile=any", moddingExe));
                }
            }
            catch { }
        }

        private void RunHiddenProcess(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = args;
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(5000);
                }
            }
            catch { }
        }

        private void CreateDesktopShortcut(string gamePath)
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string shortcutPath = Path.Combine(desktopPath, "Dead Cells Multiplayer.lnk");
                string targetBat = Path.Combine(gamePath, "Play-Multiplayer.bat");
                if (!File.Exists(targetBat)) targetBat = Path.Combine(gamePath, "multiplayer.bat");
                string iconPath = Path.Combine(gamePath, "deadcells.exe");

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType);
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = targetBat;
                    shortcut.WorkingDirectory = gamePath;
                    shortcut.Description = "Dead Cells Multiplayer Co-op";
                    if (File.Exists(iconPath))
                    {
                        shortcut.IconLocation = iconPath + ",0";
                    }
                    shortcut.Save();
                }
            }
            catch { }
        }
    }
}

using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;
using CmlLib.Core.Installer.Forge;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CmlLib.Core.ModLoaders.FabricMC;

namespace CustomLauncher
{
    public partial class MainForm : Form
    {
        // Windows API 함수 선언
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int wM_NCLBUTTONDOWN = 0xA1;
        private const int hTCAPTION = 0x2;

        private string directory;
        private string settingFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "customServer_settings.txt");
        private string versionFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "customServer_version.txt");
        private string userInfo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "customServer_udata");
        private readonly HttpClient _httpClient = new();

        private System.Windows.Forms.Timer _serverStatusTimer;

        private IWavePlayer waveOutDevice;
        private AudioFileReader audioFile;
        private string musicPath;

        private bool isMuted = false;

        public MainForm()
        {
            DebugLogger.Init(); // 디버그 로그 파일 초기화
            InitializeComponent();
            FontLibrary.Initialize(); // Initialize the font library
            ApplyFontToControls(this); // Apply the font to all controls
            this.FormBorderStyle = FormBorderStyle.None;
            this.MouseDown += new MouseEventHandler(MainForm_MouseDown);

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/108.0.0.0 Safari/537.36");

            _serverStatusTimer = new System.Windows.Forms.Timer();
            _serverStatusTimer.Interval = 10000; // 10초마다 서버 상태 확인
            _serverStatusTimer.Tick += ServerStatusTimer_Tick;
        }

        private void ApplyFontToControls(Control parentControl)
        {
            foreach (Control control in parentControl.Controls)
            {
                control.Font = new Font(FontLibrary.GetFont().FontFamily, control.Font.Size, control.Font.Style);
                if (control.HasChildren)
                {
                    ApplyFontToControls(control);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            // 리소스 정리
            if (waveOutDevice != null)
            {
                waveOutDevice.Stop();
                waveOutDevice.Dispose();
                waveOutDevice = null;
            }

            if (audioFile != null)
            {
                audioFile.Dispose();
                audioFile = null;
            }
        }

        private void MainForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        CancellationTokenSource? cancellationToken;
        MinecraftLauncher? launcher;

        private async void MainForm_Shown(object sender, EventArgs e)
        {
            var userData = LoadUserData();
            var settings = new SettingsForm();
            var installPath = LoadSettings();

            settings.saveSettings();
            directory = installPath.InstallPath;

            if (directory == null)
                directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".custom");

            try
            {
                // 디렉토리 존재하지 않으면 생성
                if (!System.IO.Directory.Exists(Directory))
                {
                    System.IO.Directory.CreateDirectory(Directory);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"디렉토리 생성 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            btnStartGame.Enabled = false;

            await CheckServerStatusAsyncs();
            await initializeLauncher(new MinecraftPath());
            _serverStatusTimer.Start();
        }

        private async void ServerStatusTimer_Tick(object sender, EventArgs e)
        {
            await CheckServerStatusAsyncs();
        }

        private async Task CheckServerStatusAsyncs()
        {
            bool isOnline = await CheckServerStatusAsync();
            UpdateServerStatusLabel(isOnline);
        }

        private const string ServerStatusUrl = "https://api.mcsrvstat.us/3/주소";

        private async Task<bool> CheckServerStatusAsync()
        {
            try
            {
                string content = await _httpClient.GetStringAsync(ServerStatusUrl);
                JObject json = JObject.Parse(content);
                return json.Value<bool>("online");
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void UpdateServerStatusLabel(bool isOnline)
        {
            if (labelServerStatus.InvokeRequired)
            {
                labelServerStatus.Invoke(new Action<bool>(UpdateServerStatusLabel), isOnline);
            }
            else
            {
                labelServerStatus.Text = isOnline ? "서버 상태:  온라인" : "서버 상태: 오프라인";
                labelServerStatus.ForeColor = isOnline ? Color.Green : Color.Red;
            }
        }

        private async Task initializeLauncher(MinecraftPath path)
        {
            try
            {
                var parameters = MinecraftLauncherParameters.CreateDefault(path, HttpClient);
                Launcher = new MinecraftLauncher(parameters);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"런처 초기화 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }



        private string ParseVersionInfo(string updateInfo)
        {
            // "버전: 1.0.1" 형식을 처리하여 버전 번호만 추출합니다.
            var lines = updateInfo.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("version :"))
                {
                    return line.Split(':')[1].Trim();
                }
            }
            return string.Empty;
        }

        private void UpdateProgressBar(int processedBytes, int totalBytes)
        {
            if (progressBar1.InvokeRequired)
            {
                progressBar1.Invoke(new Action(() => UpdateProgressBar(processedBytes, totalBytes)));
            }
            else
            {
                try
                {
                    if (totalBytes > 0 && processedBytes >= 0)
                    {
                        // 진행률이 100을 넘지 않도록 보장
                        int percentage = Math.Min((int)((double)processedBytes / totalBytes * 100), 100);
                        progressBar1.Value = Math.Max(0, Math.Min(percentage, progressBar1.Maximum));
                    }
                    else
                    {
                        progressBar1.Value = 0;
                    }
                }
                catch (Exception)
                {
                    // 오류 발생 시 진행률을 0으로 설정
                    progressBar1.Value = 0;
                }
            }
        }

        private void UpdateStatusLabel(string status)
        {
            if (labelStatus.InvokeRequired)
            {
                labelStatus.Invoke(new Action(() => UpdateStatusLabel(status)));
            }
            else
            {
                labelStatus.Text = status;
                int x = (this.ClientSize.Width - labelStatus.Width) / 2;
                labelStatus.Location = new Point(x, labelStatus.Location.Y);
            }
        }

        JELoginHandler loginHandler = JELoginHandlerBuilder.BuildDefault();


        private async void btnLogin_Click(object sender, EventArgs e)
        {
            try
            {
                // Microsoft 로그인 처리
                var session = await loginHandler.Authenticate();

                if (session == null)
                {
                    MessageBox.Show("로그인 실패: 세션 정보를 얻을 수 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Settings 객체 생성 및 로그인 정보 저장
                var settings = new Settings
                {
                    Username = session.Username,
                    Password = session.AccessToken,
                    ramValue = LoadSettings().ramValue, // 기존 설정 값 로드
                    Resolution = LoadSettings().Resolution, // 기존 설정 값 로드
                    InstallPath = LoadSettings().InstallPath // 기존 설정 값 로드
                };

                SaveUserData(settings);
                btnStartGame.Enabled = true;
                btnLogout.Enabled = true;
                btnLogin.Enabled = false;
                btnLogin.Visible = false;
                btnStartGame.Visible = true;
                btnLogout.Visible = true;
            }
            catch (Exception ex)
            {
                await LoginHandler.Signout();
                MessageBox.Show($"로그인 또는 실행 실패: 다시 시도하세요.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void btnLogout_Click(object sender, EventArgs e)
        {
            btnStartGame.Enabled = false;
            btnLogout.Enabled = false;
            btnLogin.Enabled = true;
            btnLogin.Visible = true;
            btnStartGame.Visible = false;
            btnLogout.Visible = false;
            try
            {
                await LoginHandler.Signout();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private async void btnStartGame_Click(object sender, EventArgs e)
        {
            DebugLogger.Log("btnStartGame_Click entered.");
            // 버튼 클릭 즉시 비활성화
            btnStartGame.Enabled = false;

            var installPath = LoadSettings();
            directory = installPath.InstallPath;
            MinecraftPath myPath = new MinecraftPath(Path.Combine(directory));
            try
            {
                var launcher = new MinecraftLauncher(Directory);
                var versions = await launcher.GetAllVersionsAsync();
                var fabricInstaller = new FabricInstaller(new HttpClient());

                // 매니페스트 기반 자동 업데이트 실행
                UpdateStatusLabel("업데이트 확인 중...");
                bool updatesFound = await AutoUpdater.CheckForUpdatesAsync(directory);
                if (updatesFound)
                {
                    UpdateStatusLabel("업데이트 완료.");
                }
                else
                {
                    UpdateStatusLabel("최신 버전입니다.");
                }

                // Forge 버전 정보
                const string mcVersion = "버전";
                const string forgeVersion = "버전";
                string forgeVersionName = $"{mcVersion}-forge-{mcVersion}-{forgeVersion}";

                // 설치된 버전 확인
                bool forgeInstalled = versions.Any(v => v.Name.Equals(forgeVersionName, StringComparison.OrdinalIgnoreCase));

                if (!forgeInstalled)
                {
                    UpdateStatusLabel("Forge를 설치합니다...");

                    // Forge 설치 진행률 표시 설정
                    var fileProgress = new Progress<InstallerProgressChangedEventArgs>(e =>
                    {
                        UpdateStatusLabel($"[{e.EventType}] {e.Name} ({e.ProgressedTasks}/{e.TotalTasks})");
                        UpdateProgressBar(e.ProgressedTasks, e.TotalTasks);
                    });

                    var byteProgress = new Progress<ByteProgress>(e =>
                    {
                        int percentage = (int)(e.ToRatio() * 100);
                        UpdateProgressBar(percentage, 100);
                    });

                    var installerOutput = new Progress<string>(e =>
                    {
                        Console.WriteLine(e);
                    });

                    // Forge 설치 실행
                    var forge = new ForgeInstaller(launcher);
                    var version_name = await forge.Install(mcVersion, forgeVersion, new ForgeInstallOptions
                    {
                        FileProgress = fileProgress,
                        ByteProgress = byteProgress,
                        InstallerOutput = installerOutput
                    });

                    Console.WriteLine($"설치된 Forge 버전: {version_name}");
                    forgeVersionName = version_name;
                }

                var session = await loginHandler.Authenticate();
                var settings = LoadSettings();
                var settingsForm = new SettingsForm();
                var resolution = settingsForm.GetSelectedResolution();

                // Create LaunchOption
                var launchOption = new MLaunchOption
                {
                    Session = new MSession
                    {
                        Username = session.Username,
                        AccessToken = session.AccessToken,
                        UUID = session.UUID,
                        Xuid = session.Xuid
                    },
                    ServerIp = "서버주소",
                    ScreenWidth = resolution[0],
                    ScreenHeight = resolution[1],
                    GameLauncherName = "SERVER",
                };

                int ramValue;
                bool isValidRamValue = int.TryParse(settings.ramValue, out ramValue);

                if (isValidRamValue)
                {
                    launchOption.MaximumRamMb = ramValue;
                    launchOption.MinimumRamMb = ramValue;
                }

                // Update UI
                UpdateStatusLabel("게임 실행 중...");
                progressBar1.Visible = true;
                progressBar1.Style = ProgressBarStyle.Continuous;
                progressBar1.Value = 0;

                btnStartGame.Enabled = false;
                UpdateProgressBar(20, 100);

                var process = await launcher.CreateProcessAsync(forgeVersionName, launchOption);

                UpdateProgressBar(50, 100);

                // 프로세스 이벤트 설정
                var processUtil = new ProcessWrapper(process);
                processUtil.OutputReceived += (s, args) => UpdateProcessOutput(args);
                processUtil.Exited += (s, args) => OnGameProcessExited();  // 게임 종료 이벤트 추가
                processUtil.StartWithEvents();

                UpdateProgressBar(100, 100);

                await processUtil.WaitForExitTaskAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"게임 시작 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                EnableStartGameButton(); // 오류 발생 시에도 버튼 활성화
            }
        }

        private void UpdateProcessOutput(string output)
        {
            if (!string.IsNullOrEmpty(output))
            {
                Console.WriteLine(output);
            }
        }

        private void OnGameProcessExited()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(OnGameProcessExited));
                return;
            }

            EnableStartGameButton();
            UpdateStatusLabel("");
            progressBar1.Value = 0;
        }

        private void EnableStartGameButton()
        {
            if (btnStartGame.InvokeRequired)
            {
                btnStartGame.Invoke(new Action(EnableStartGameButton));
                return;
            }

            btnStartGame.Enabled = true;
        }

        private Settings LoadSettings()
        {
            var settings = new Settings();

            if (File.Exists(SettingFile))
            {
                try
                {
                    var settingsLines = File.ReadAllLines(SettingFile);
                    if (settingsLines.Length >= 3)
                    {
                        settings.Resolution = settingsLines[0];
                        settings.InstallPath = settingsLines[1];
                        settings.ramValue = settingsLines[2];
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"설정 로드 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return settings;
        }

        private Settings LoadUserData()
        {
            var settings = new Settings();

            if (File.Exists(UserInfo))
            {
                try
                {
                    var encryptedData = File.ReadAllBytes(UserInfo);
                    var decryptedData = EncryptionHelper.Decrypt(encryptedData);
                    var settingsLines = decryptedData.Split('\n');

                    if (settingsLines.Length >= 2)
                    {
                        settings.Username = settingsLines[0];
                        settings.Password = settingsLines[1];
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"설정 로드 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return settings;
        }

        private Settings LoadVersion()
        {
            var settings = new Settings();

            if (File.Exists(versionFilePath))
            {
                try
                {
                    var versionData = File.ReadAllLines(versionFilePath);
                    if (versionData.Length == 1)
                    {
                        settings.versionData = versionData[0];
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"설정 로드 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return settings;
        }

        private void SaveUserData(Settings settings)
        {
            try
            {
                var settingsString = $"{settings.Username}\n{settings.Password}";
                var encryptedData = EncryptionHelper.Encrypt(settingsString);
                File.WriteAllBytes(UserInfo, encryptedData);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"설정 저장 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        InstallerProgressChangedEventArgs? fileProgress;

        public static int WM_NCLBUTTONDOWN => wM_NCLBUTTONDOWN;

        public static int HTCAPTION => hTCAPTION;

        public string Directory { get => directory; set => directory = value; }
        public string SettingFile { get => settingFile; set => settingFile = value; }
        public string UserInfo { get => userInfo; set => userInfo = value; }

        public HttpClient HttpClient => _httpClient;

        public string AppDataPath { get => appDataPath; set => appDataPath = value; }
        public CancellationTokenSource CancellationToken { get => cancellationToken; set => cancellationToken = value; }
        public MinecraftLauncher Launcher { get => launcher; set => launcher = value; }
        public JELoginHandler LoginHandler { get => loginHandler; set => loginHandler = value; }
        public InstallerProgressChangedEventArgs FileProgress { get => fileProgress; set => fileProgress = value; }


        private void btnSettings1_Click(object sender, EventArgs e)
        {
            SettingsForm settingsForm = new SettingsForm();
            settingsForm.ShowDialog();
        }

        private void btnSettings2_Click(object sender, EventArgs e)
        {
            SettingsForm settingsForm = new SettingsForm();
            settingsForm.ShowDialog();
        }

        private class Settings
        {
            public string Username { get; set; }
            public string Password { get; set; }
            public string ramValue { get; set; }
            public string Resolution { get; set; }
            public string InstallPath { get; set; }
            public string versionData { get; set; }
        }

        private void EXIT_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void btnLogin_MouseDown(object sender, MouseEventArgs e)
        {
            btnLogin.BackgroundImage = new Bitmap(Properties.Resources.login_clicked);
        }

        private void btnLogin_MouseUp(object sender, MouseEventArgs e)
        {
            btnLogin.BackgroundImage = new Bitmap(Properties.Resources.login);
        }

        private void btnStartGame_MouseDown(object sender, MouseEventArgs e)
        {
            btnStartGame.BackgroundImage = new Bitmap(Properties.Resources.gamestart_clicked);
        }

        private void btnStartGame_MouseUp(object sender, MouseEventArgs e)
        {
            btnStartGame.BackgroundImage = new Bitmap(Properties.Resources.gamestart);
        }

        private void btnLogout_MouseDown(object sender, MouseEventArgs e)
        {
            btnLogout.BackgroundImage = new Bitmap(Properties.Resources.logout_clicked);
        }

        private void btnLogout_MouseUp(object sender, MouseEventArgs e)
        {
            btnLogout.BackgroundImage = new Bitmap(Properties.Resources.logout);
        }

        private void settingsBtn_MouseDown(object sender, MouseEventArgs e)
        {
            settingsBtn.BackgroundImage = new Bitmap(Properties.Resources.config_clicked);
        }

        private void settingsBtn_MouseUp(object sender, MouseEventArgs e)
        {
            settingsBtn.BackgroundImage = new Bitmap(Properties.Resources.config);
        }
    }
}

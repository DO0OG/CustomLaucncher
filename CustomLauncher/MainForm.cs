using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Installers;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.ProcessBuilder;
using CustomLauncher.Core;
using CustomLauncher.Models;
using NAudio.Wave;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CustomLauncher
{
    public partial class MainForm : Form
    {
        // 타이틀 바 드래그 이동을 위한 Windows API
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int wM_NCLBUTTONDOWN = 0xA1;
        private const int hTCAPTION = 0x2;

        private string directory;
        private readonly HttpClient _httpClient = new();

        // 서버 상태 주기적 확인 타이머
        private System.Windows.Forms.Timer _serverStatusTimer;
        private ServerStatusChecker _serverStatusChecker;

        // 배경음악 재생 관련 필드
        private IWavePlayer waveOutDevice;
        private AudioFileReader audioFile;
        private string musicPath;
        private bool isMuted = false;

        string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        CancellationTokenSource? cancellationToken;
        MinecraftLauncher? launcher;
        JELoginHandler loginHandler = JELoginHandlerBuilder.BuildDefault();
        InstallerProgressChangedEventArgs? fileProgress;

        public MainForm()
        {
            DebugLogger.Init(); // 디버그 로그 파일 초기화
            InitializeComponent();
            FontLibrary.Initialize();
            FontLibrary.ApplyToControls(this); // 전체 컨트롤에 DNFBitBitv2 폰트 적용
            this.FormBorderStyle = FormBorderStyle.None;
            this.MouseDown += new MouseEventHandler(MainForm_MouseDown);

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/108.0.0.0 Safari/537.36");

            _serverStatusChecker = new ServerStatusChecker(_httpClient);
            _serverStatusTimer = new System.Windows.Forms.Timer();
            _serverStatusTimer.Interval = 10000; // 10초마다 서버 상태 확인
            _serverStatusTimer.Tick += ServerStatusTimer_Tick;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);

            // 배경음악 리소스 해제
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
            // 좌클릭 드래그로 창 이동
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        private async void MainForm_Shown(object sender, EventArgs e)
        {
            var settings = new SettingsForm();
            var installSettings = AppSettingsManager.Load();

            settings.saveSettings();
            directory = installSettings.InstallPath;

            if (directory == null)
                directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LauncherConfig.DefaultInstallFolderName);

            try
            {
                // 설치 디렉토리가 없으면 생성
                if (!System.IO.Directory.Exists(directory))
                    System.IO.Directory.CreateDirectory(directory);
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

            // 저장된 사용자 데이터가 있으면 자동 로그인 시도, 없으면 CmlLib 캐시도 초기화
            var userData = UserDataManager.Load();
            if (!string.IsNullOrEmpty(userData.Username))
            {
                try
                {
                    var session = await loginHandler.Authenticate();
                    if (session != null)
                    {
                        UserDataManager.Save(session.Username, session.AccessToken);
                        btnStartGame.Enabled = true;
                        btnLogout.Enabled = true;
                        btnLogin.Enabled = false;
                        btnLogin.Visible = false;
                        btnStartGame.Visible = true;
                        btnLogout.Visible = true;
                    }
                }
                catch
                {
                    // 자동 로그인 실패 시 CmlLib 캐시 초기화 후 로그인 버튼 표시
                    await loginHandler.Signout();
                }
            }
            else
            {
                // udata 없음 = 미로그인 상태로 간주, CmlLib 캐시도 초기화
                await loginHandler.Signout();
            }
        }

        private async void ServerStatusTimer_Tick(object sender, EventArgs e)
        {
            await CheckServerStatusAsyncs();
        }

        /// <summary>
        /// 서버 상태를 확인하고 UI 레이블을 업데이트합니다.
        /// </summary>
        private async Task CheckServerStatusAsyncs()
        {
            bool isOnline = await _serverStatusChecker.CheckAsync();
            UpdateServerStatusLabel(isOnline);
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
                        // 진행률이 0~100 범위를 벗어나지 않도록 보정
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
                // 레이블을 수평 가운데 정렬
                int x = (this.ClientSize.Width - labelStatus.Width) / 2;
                labelStatus.Location = new Point(x, labelStatus.Location.Y);
            }
        }

        private async void btnLogin_Click(object sender, EventArgs e)
        {
            try
            {
                // Microsoft(Xbox) 계정 인증
                var session = await loginHandler.Authenticate();

                if (session == null)
                {
                    MessageBox.Show("로그인 실패: 세션 정보를 얻을 수 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // 인증 토큰을 AES 암호화하여 파일에 저장
                UserDataManager.Save(session.Username, session.AccessToken);

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
                MessageBox.Show("로그인 또는 실행 실패: 다시 시도하세요.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            DebugLogger.Log("btnStartGame_Click 진입.");
            btnStartGame.Enabled = false;

            var installSettings = AppSettingsManager.Load();
            directory = installSettings.InstallPath;
            MinecraftPath myPath = new MinecraftPath(Path.Combine(directory));

            try
            {
                var launcher = new MinecraftLauncher(directory);
                var versions = await launcher.GetAllVersionsAsync();
                var fabricInstaller = new FabricInstaller(new HttpClient());

                // 매니페스트 기반 자동 업데이트 실행
                UpdateStatusLabel("업데이트 확인 중...");
                bool updatesFound = await AutoUpdater.CheckForUpdatesAsync(directory);
                UpdateStatusLabel(updatesFound ? "업데이트 완료." : "최신 버전입니다.");

                // Forge 버전 정보
                const string mcVersion = LauncherConfig.McVersion;
                const string forgeVersion = LauncherConfig.ForgeVersion;
                string forgeVersionName = $"{mcVersion}-forge-{mcVersion}-{forgeVersion}";

                // 설치된 Forge 버전 확인
                bool forgeInstalled = versions.Any(v => v.Name.Equals(forgeVersionName, StringComparison.OrdinalIgnoreCase));

                if (!forgeInstalled)
                {
                    UpdateStatusLabel("Forge를 설치합니다...");

                    var fileProgress = new Progress<InstallerProgressChangedEventArgs>(ev =>
                    {
                        UpdateStatusLabel($"[{ev.EventType}] {ev.Name} ({ev.ProgressedTasks}/{ev.TotalTasks})");
                        UpdateProgressBar(ev.ProgressedTasks, ev.TotalTasks);
                    });

                    var byteProgress = new Progress<ByteProgress>(ev =>
                    {
                        int percentage = (int)(ev.ToRatio() * 100);
                        UpdateProgressBar(percentage, 100);
                    });

                    var installerOutput = new Progress<string>(ev =>
                    {
                        Console.WriteLine(ev);
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
                var settingsForm = new SettingsForm();
                var resolution = settingsForm.GetSelectedResolution();

                // 게임 실행 옵션 구성
                var launchOption = new MLaunchOption
                {
                    Session = new MSession
                    {
                        Username = session.Username,
                        AccessToken = session.AccessToken,
                        UUID = session.UUID,
                        Xuid = session.Xuid
                    },
                    ServerIp = LauncherConfig.ServerIp,
                    ScreenWidth = resolution[0],
                    ScreenHeight = resolution[1],
                    GameLauncherName = LauncherConfig.GameLauncherName,
                };

                // RAM 설정 적용
                if (int.TryParse(installSettings.RamValue, out int ramMb))
                {
                    launchOption.MaximumRamMb = ramMb;
                    launchOption.MinimumRamMb = ramMb;
                }

                UpdateStatusLabel("게임 실행 중...");
                progressBar1.Visible = true;
                progressBar1.Style = ProgressBarStyle.Continuous;
                progressBar1.Value = 0;

                btnStartGame.Enabled = false;
                UpdateProgressBar(20, 100);

                var process = await launcher.CreateProcessAsync(forgeVersionName, launchOption);

                UpdateProgressBar(50, 100);

                // 프로세스 이벤트 설정 및 시작
                var processUtil = new ProcessWrapper(process);
                processUtil.OutputReceived += (s, args) => UpdateProcessOutput(args);
                processUtil.Exited += (s, args) => OnGameProcessExited(); // 게임 종료 이벤트
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
                Console.WriteLine(output);
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

        // Windows 메시지 상수 프로퍼티
        public static int WM_NCLBUTTONDOWN => wM_NCLBUTTONDOWN;
        public static int HTCAPTION => hTCAPTION;

        public string Directory { get => directory; set => directory = value; }
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

        private void EXIT_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        // 버튼 이미지 마우스 이벤트 핸들러
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

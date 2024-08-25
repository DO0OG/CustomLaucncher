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
        private string settingFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"dog_settings.txt");
        private string versionFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dog_version.txt");
        private string userInfo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dog_udata");
        private readonly HttpClient _httpClient = new();

        private System.Windows.Forms.Timer _serverStatusTimer;

        public MainForm()
        {
            InitializeComponent();
            this.FormBorderStyle = FormBorderStyle.None;
            this.MouseDown += new MouseEventHandler(MainForm_MouseDown);

            _serverStatusTimer = new System.Windows.Forms.Timer();
            _serverStatusTimer.Interval = 10000; // 10초마다 서버 상태 확인
            _serverStatusTimer.Tick += ServerStatusTimer_Tick;
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
                directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".dogserver");

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

        private const string ServerStatusUrl = "https://mcv.kr/mchecker/api.php?address=dogs.mcv.kr&port=25565&autodns=1";

        private async Task<bool> CheckServerStatusAsync()
        {
            try
            {
                string content = await _httpClient.GetStringAsync(ServerStatusUrl);

                // "online" 필드의 값이 true인지 확인
                bool isOnline;

                if (content == "1") isOnline = true;
                else if (content == "2") isOnline = false;
                else isOnline = false;

                return isOnline;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}");
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
            var parameters = MinecraftLauncherParameters.CreateDefault(path, HttpClient);
            Launcher = new MinecraftLauncher(parameters);
        }

        private async Task CheckForRequiredFiles()
        {
            var settings = LoadSettings();
            string gameDirectory = settings.InstallPath;
            string minecraftDirectory = Path.Combine(directory);
            string serverDatFilePath = Path.Combine(minecraftDirectory, "servers.dat");
            string modsFolderPath = Path.Combine(minecraftDirectory, "mods");

            try
            {
                if (!System.IO.Directory.Exists(minecraftDirectory))
                {
                    System.IO.Directory.CreateDirectory(minecraftDirectory);
                }

                if (File.Exists(serverDatFilePath) && System.IO.Directory.Exists(modsFolderPath) && System.IO.Directory.GetFiles(modsFolderPath).Length > 0)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"디렉토리 생성 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string[] requiredFiles = { "1201.7z.001", "1201.7z.002", "1201.7z.003" };

            foreach (var file in requiredFiles)
            {
                string filePath = Path.Combine(minecraftDirectory, file);

                if (!File.Exists(filePath))
                {
                    try
                    {
                        progressBar1.Value = 0;
                        progressBar1.Visible = true;
                        UpdateStatusLabel("파일 다운로드 중...");

                        await DownloadFilesAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"파일 다운로드 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    break;
                }
            }

            try
            {
                await Ensure7ZipInstalledAsync();

                UpdateStatusLabel("압축 해제 중...");
                await ExtractArchiveAsync(Path.Combine(minecraftDirectory, "1201.7z.001"), minecraftDirectory, UpdateProgressBar);

                foreach (var file in requiredFiles)
                {
                    string filePath = Path.Combine(minecraftDirectory, file);
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"압축 해제 실패: {ex.Message}\n{minecraftDirectory}\n{Path.Combine(minecraftDirectory, "1201.7z.001")}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                progressBar1.Value = 0;
                UpdateStatusLabel("     작업 완료");
            }
        }

        private async Task Ensure7ZipInstalledAsync()
        {
            string programFilesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7Ziptemp");
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;

            bool is64BitOperatingSystem = Environment.Is64BitOperatingSystem;

            byte[] zipFileData;

            if (is64BitOperatingSystem)
            {
                zipFileData = Properties.Resources._7_Zip_x64;
            }
            else
            {
                zipFileData = Properties.Resources._7_Zip_x86;
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "7ZipTemp");
            if (!System.IO.Directory.Exists(tempDirectory))
            {
                System.IO.Directory.CreateDirectory(tempDirectory);
            }

            string sevenZipExePath = Path.Combine(programFilesPath, "7z.exe");

            if (!System.IO.Directory.Exists(programFilesPath))
            {
                System.IO.Directory.CreateDirectory(programFilesPath);
            }

            if (!File.Exists(sevenZipExePath))
            {
                try
                {
                    UpdateStatusLabel("7-Zip을 설치 중입니다...");

                    string tempZipPath = Path.Combine(tempDirectory, ".zip");
                    File.WriteAllBytes(tempZipPath, zipFileData);

                    await ExtractArchiveAsync(tempZipPath, programFilesPath);

                    if (!File.Exists(sevenZipExePath))
                    {
                        throw new InvalidOperationException("7-Zip 압축 해제 실패.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"7-Zip 설치 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (System.IO.Directory.Exists(tempDirectory))
                    {
                        System.IO.Directory.Delete(tempDirectory, true);
                    }
                }
            }
        }

        private async Task ExtractArchiveAsync(string zipPath, string extractPath)
        {
            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"압축 해제 실패: {ex.Message}", ex);
            }
        }

        private async Task DownloadFilesAsync()
        {
            string minecraftDirectory = Path.Combine(directory);

            string filePath1 = Path.Combine(minecraftDirectory, "1201.7z.001");
            string filePath2 = Path.Combine(minecraftDirectory, "1201.7z.002");
            string filePath3 = Path.Combine(minecraftDirectory, "1201.7z.003");

            using (HttpClient client = new HttpClient())
            {
                var fileUrls = new[]
                {
                    ("https://dogs.kro.kr/f/d630386bfdf44a76b1c2/?dl=1", filePath1),
                    ("https://dogs.kro.kr/f/5411603bb260427d8ed6/?dl=1", filePath2),
                    ("https://dogs.kro.kr/f/303e340dd67742bea074/?dl=1", filePath3)
                }; 

                foreach (var (url, filePath) in fileUrls)
                {
                    if (File.Exists(filePath))
                    {
                        Console.WriteLine($"{filePath} already exists. Skipping download.");
                        continue;
                    }

                    await DownloadFileAsync(client, url, filePath);
                }
            }
        }

        private async Task DownloadFileAsync(HttpClient client, string url, string outputPath)
        {
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192];
                        int bytesRead;
                        long totalBytesRead = 0;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalBytesRead += bytesRead;

                            UpdateProgressBar((int)totalBytesRead, (int)totalBytes);
                        }
                    }
                }
            }
        }

        private async Task ExtractArchiveAsync(string inFile, string outputFolder, Action<int, int> updateProgress)
        {
            await Task.Run(() =>
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7Ziptemp", "7z.exe"),
                    Arguments = $"x \"{inFile}\" -o\"{outputFolder}\" -y",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null) throw new InvalidOperationException("7z.exe process could not be started.");

                    using (var outputReader = process.StandardOutput)
                    using (var errorReader = process.StandardError)
                    {
                        string output = outputReader.ReadToEnd();
                        string error = errorReader.ReadToEnd();
                        process.WaitForExit();

                        if (!string.IsNullOrEmpty(output))
                        {
                            Console.WriteLine("7z Output: " + output);
                        }

                        if (!string.IsNullOrEmpty(error))
                        {
                            Console.WriteLine("7z Error: " + error);
                        }

                        if (process.ExitCode != 0)
                        {
                            throw new InvalidOperationException($"7z process failed with exit code {process.ExitCode}.");
                        }
                    }
                }
            });
        }

        private async Task CheckForUpdatesAsync()
        {
            string updateInfoUrl = "https://dogdev.buzz/update-info.txt";

            var versionData = LoadVersion();
            string currentVersion = versionData.versionData;

            try
            {
                // 최신 버전 정보를 가져옵니다.
                string updateInfo = await _httpClient.GetStringAsync(updateInfoUrl);
                var versionInfo = ParseVersionInfo(updateInfo);

                if (versionInfo != currentVersion)
                {
                    progressBar1.Value = 0;
                    progressBar1.Visible = true;
                    UpdateStatusLabel("업데이트 중...");

                    DeleteOldFiles();

                    await DownloadFilesAsync();

                    File.WriteAllText(versionFilePath, versionInfo);

                    try
                    {
                        await Ensure7ZipInstalledAsync();

                        UpdateStatusLabel("압축 해제 중...");
                        await ExtractArchiveAsync(Path.Combine(directory, "1201.7z.001"), directory, UpdateProgressBar);

                        string[] requiredFiles = { "1201.7z.001", "1201.7z.002", "1201.7z.003" };

                        foreach (var file in requiredFiles)
                        {
                            string filePath = Path.Combine(directory, file);
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"압축 해제 실패: {ex.Message}\n{directory}\n{Path.Combine(directory, "1201.7z.001")}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    finally
                    {
                        progressBar1.Value = 0;
                        UpdateStatusLabel("업데이트 완료");
                    }
                }
                else
                {
                    UpdateStatusLabel("최신 버전입니다.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusLabel("업데이트 체크 중 오류 발생");
            }
        }

        private void DeleteOldFiles()
        {
            string[] directoriesToDelete = { "mods" };

            foreach (var dir in directoriesToDelete)
            {
                string fullPath = Path.Combine(directory, dir);

                if (System.IO.Directory.Exists(fullPath))
                {
                    try
                    {
                        // 폴더 내의 모든 파일 및 서브디렉토리 삭제
                        foreach (var file in System.IO.Directory.GetFiles(fullPath))
                        {
                            File.Delete(file);
                        }

                        foreach (var subDir in System.IO.Directory.GetDirectories(fullPath))
                        {
                            System.IO.Directory.Delete(subDir, true); // true to delete subdirectories
                        }

                        // 빈 폴더 삭제
                        System.IO.Directory.Delete(fullPath);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"폴더 삭제 실패: {ex.Message}\n{fullPath}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
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
                if (totalBytes > 0)
                {
                    progressBar1.Value = (int)((double)processedBytes / totalBytes * 100);
                }
                else
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
            var installPath = LoadSettings();
            directory = installPath.InstallPath;
            MinecraftPath myPath = new MinecraftPath(Path.Combine(directory));
            try
            {
                var launcher = new MinecraftLauncher(Directory);
                var versions = await launcher.GetAllVersionsAsync();
                foreach (var v in versions)
                {
                    Console.WriteLine(v.Name);
                }

                // 파일 확인, 다운로드 및 압축 해제 작업 수행
                await CheckForRequiredFiles();
                await CheckForUpdatesAsync();

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
                    ServerIp = "dogs.mcv.kr",
                    ScreenWidth = resolution[0],
                    ScreenHeight = resolution[1],
                    GameLauncherName = "DOG SERVER",
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

                CancellationToken = new CancellationTokenSource();

                var version = "fabric-loader-0.15.11-1.20.1";
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                btnStartGame.Enabled = false;
                UpdateProgressBar(20, 100);

                var process = await launcher.CreateProcessAsync(version, launchOption);

                UpdateProgressBar(50, 100);

                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
                process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
                process.EnableRaisingEvents = true;

                process.OutputDataReceived += (s, args) => UpdateProcessOutput(args.Data);
                process.ErrorDataReceived += (s, args) => UpdateProcessOutput(args.Data);
                process.Exited += (s, args) => OnProcessExited();

                UpdateProgressBar(80, 100);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                UpdateProgressBar(100, 100); // Completed

                await Task.Run(() => process.WaitForExit()); // Wait for the process to exit
            }
            catch (Exception ex)
            {
                MessageBox.Show($"게임 시작 중 오류 발생: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateProcessOutput(string data)
        {
            if (!string.IsNullOrEmpty(data))
            {
                Console.WriteLine(data);
            }
        }

        private void OnProcessExited()
        {
            if (labelStatus.InvokeRequired || progressBar1.InvokeRequired)
            {
                labelStatus.Invoke(new Action(() => UpdateStatusLabel("")));
                progressBar1.Invoke(new Action(() => progressBar1.Value = 0));
                progressBar1.Invoke(new Action(() => btnStartGame.Enabled = true));
            }
            else
            {
                UpdateStatusLabel("");
                progressBar1.Value = 0;
                btnStartGame.Enabled = true;
            }
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

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            string url = "https://dogpub.p-e.kr";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void EXIT_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void btnLogin_MouseDown(object sender, MouseEventArgs e)
        {
            btnLogin.BackgroundImage = new Bitmap(Properties.Resources.Login_Clicked);
        }

        private void btnLogin_MouseUp(object sender, MouseEventArgs e)
        {
            btnLogin.BackgroundImage = new Bitmap(Properties.Resources.Login);
        }

        private void btnStartGame_MouseDown(object sender, MouseEventArgs e)
        {
            btnStartGame.BackgroundImage = new Bitmap(Properties.Resources.GameStart_Clicked);
        }

        private void btnStartGame_MouseUp(object sender, MouseEventArgs e)
        {
            btnStartGame.BackgroundImage = new Bitmap(Properties.Resources.GameStart);
        }

        private void btnLogout_MouseDown(object sender, MouseEventArgs e)
        {
            btnLogout.BackgroundImage = new Bitmap(Properties.Resources.Logout_Clicked);
        }

        private void btnLogout_MouseUp(object sender, MouseEventArgs e)
        {
            btnLogout.BackgroundImage = new Bitmap(Properties.Resources.Logout);
        }

        private void settingsBtn_MouseDown(object sender, MouseEventArgs e)
        {
            settingsBtn.BackgroundImage = new Bitmap(Properties.Resources.setting_clicked);
        }

        private void settingsBtn_MouseUp(object sender, MouseEventArgs e)
        {
            settingsBtn.BackgroundImage = new Bitmap(Properties.Resources.setting);
        }
    }
}

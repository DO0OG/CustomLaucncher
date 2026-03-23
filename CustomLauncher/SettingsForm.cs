using System;
using System.Drawing;
using System.Windows.Forms;
using CustomLauncher.Core;

namespace CustomLauncher
{
    public partial class SettingsForm : Form
    {
        public SettingsForm()
        {
            InitializeComponent();
            PopulateResolutionComboBox();

            // 기본 설치 경로 설정
            string defaultPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                LauncherConfig.DefaultInstallFolderName
            );
            txtInstallPath.Text = defaultPath;

            LoadSettings();

            // 폰트 적용은 Load 이벤트에서 수행
            this.Load += SettingsForm_Load;
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            try
            {
                FontLibrary.ApplyToControls(this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"폰트 적용 오류: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            // AppSettingsManager를 통해 설정 파일에서 불러옴
            var settings = AppSettingsManager.Load();

            if (settings.Resolution != null)
                cboResolution.SelectedItem = settings.Resolution;
            if (settings.InstallPath != null)
                txtInstallPath.Text = settings.InstallPath;
            if (settings.RamValue != null)
                ramValue.Text = settings.RamValue;
        }

        private void PopulateResolutionComboBox()
        {
            cboResolution.Items.Add("800x600");
            cboResolution.Items.Add("1280x720");
            cboResolution.Items.Add("1600x1200");
            cboResolution.Items.Add("1920x1080");
            cboResolution.Items.Add("2560x1440");

            if (cboResolution.Items.Count > 0)
                cboResolution.SelectedIndex = 3; // 기본값: 1920x1080
        }

        /// <summary>
        /// 현재 선택된 해상도를 [너비, 높이] 배열로 반환합니다.
        /// 파싱 실패 시 기본값 [1920, 1080]을 반환합니다.
        /// </summary>
        public int[] GetSelectedResolution()
        {
            string selected = cboResolution.SelectedItem?.ToString() ?? "1920x1080";
            var parts = selected.Split('x');

            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int width) &&
                int.TryParse(parts[1], out int height))
            {
                return new int[] { width, height };
            }

            return new int[] { 1920, 1080 };
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            saveSettings();
            MessageBox.Show("설정이 저장되었습니다.");
            this.Close();
        }

        /// <summary>
        /// 현재 UI 설정값을 AppSettingsManager를 통해 파일에 저장합니다.
        /// </summary>
        public void saveSettings()
        {
            AppSettingsManager.Save(
                cboResolution.SelectedItem?.ToString() ?? "1920x1080",
                txtInstallPath.Text,
                ramValue.Text
            );
        }

        private void btnBrowsePath_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
                    txtInstallPath.Text = folderDialog.SelectedPath;
            }
        }

        private void btnAbout_Click(object sender, EventArgs e)
        {
            using (AboutForm aboutForm = new AboutForm())
            {
                aboutForm.ShowDialog();
            }
        }
    }
}

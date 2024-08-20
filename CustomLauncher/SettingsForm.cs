using System;
using System.IO;
using System.Windows.Forms;

namespace CustomLauncher
{
    public partial class SettingsForm : Form
    {
        private string settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dog_settings.txt");
        private string versionFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dog_version.txt");

        public string SettingsFilePath { get => settingsFilePath; set => settingsFilePath = value; }
        public string VersionFilePath { get => versionFilePath; set => versionFilePath = value; }

        public SettingsForm()
        {
            InitializeComponent();
            PopulateResolutionComboBox();
            LoadSettings();
            LoadVersionInfo();
        }

        private void LoadSettings()
        {
            if (File.Exists(SettingsFilePath))
            {
                var settings = File.ReadAllLines(SettingsFilePath);
                if (settings.Length >= 3)
                {
                    cboResolution.SelectedItem = settings[0];
                    txtInstallPath.Text = settings[1];
                    ramValue.Text = settings[2];
                }
            }
        }

        private void LoadVersionInfo()
        {
            if (File.Exists(VersionFilePath))
            {
                var versionInfo = File.ReadAllText(VersionFilePath);
                version.Text = $"현재 버전: {versionInfo}"; // 라벨에 현재 버전 표시
            }
            else
            {
                var settings = new string[]
                {
                    "1.0.0.0"
                };

                File.WriteAllLines(VersionFilePath, settings);
            }
        }

        private void PopulateResolutionComboBox()
        {
            cboResolution.Items.Add("800x600");
            cboResolution.Items.Add("1280x720");
            cboResolution.Items.Add("1600x1200");
            cboResolution.Items.Add("1920x1080");
            cboResolution.Items.Add("2560x1440");

            if (cboResolution.Items.Count > 0)
            {
                cboResolution.SelectedIndex = 3;
            }
        }

        public int[] GetSelectedResolution()
        {
            string selectedResolution = cboResolution.SelectedItem?.ToString() ?? "1920x1080"; // 기본값으로 1920x1080 설정

            var resolutionParts = selectedResolution.Split('x');

            if (resolutionParts.Length == 2 &&
                int.TryParse(resolutionParts[0], out int width) &&
                int.TryParse(resolutionParts[1], out int height))
            {
                return new int[] { width, height };
            }
            else
            {
                return new int[] { 1920, 1080 };
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            var main = new MainForm();

            saveSettings();

            MessageBox.Show("설정이 저장되었습니다.");
            this.Close();
        }

        public void saveSettings()
        {
            var settings = new string[]
            {
                cboResolution.SelectedItem.ToString(),
                txtInstallPath.Text,
                ramValue.Text
            };

            File.WriteAllLines(SettingsFilePath, settings);
        }

        private void btnBrowsePath_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    txtInstallPath.Text = folderDialog.SelectedPath;
                }
            }
        }
    }
}

using System.IO;
using System;

namespace CustomLauncher
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SettingsForm));
            this.cboResolution = new System.Windows.Forms.ComboBox();
            this.txtInstallPath = new System.Windows.Forms.TextBox();
            this.lblResolution = new System.Windows.Forms.Label();
            this.lblInstallPath = new System.Windows.Forms.Label();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnBrowsePath = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.ramValue = new System.Windows.Forms.TextBox();
            this.version = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // cboResolution
            // 
            this.cboResolution.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboResolution.FormattingEnabled = true;
            this.cboResolution.Location = new System.Drawing.Point(108, 73);
            this.cboResolution.Name = "cboResolution";
            this.cboResolution.Size = new System.Drawing.Size(297, 20);
            this.cboResolution.TabIndex = 3;
            // 
            // txtInstallPath
            // 
            this.txtInstallPath.Location = new System.Drawing.Point(108, 27);
            this.txtInstallPath.Name = "txtInstallPath";
            this.txtInstallPath.Size = new System.Drawing.Size(188, 21);
            this.txtInstallPath.TabIndex = 4;
            this.txtInstallPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".dogserver");
            // 
            // lblResolution
            // 
            this.lblResolution.AutoSize = true;
            this.lblResolution.Font = new System.Drawing.Font("던파 비트비트체 v2", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.lblResolution.Location = new System.Drawing.Point(24, 72);
            this.lblResolution.Name = "lblResolution";
            this.lblResolution.Size = new System.Drawing.Size(58, 23);
            this.lblResolution.TabIndex = 8;
            this.lblResolution.Text = "해상도";
            // 
            // lblInstallPath
            // 
            this.lblInstallPath.AutoSize = true;
            this.lblInstallPath.Font = new System.Drawing.Font("던파 비트비트체 v2", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.lblInstallPath.Location = new System.Drawing.Point(14, 26);
            this.lblInstallPath.Name = "lblInstallPath";
            this.lblInstallPath.Size = new System.Drawing.Size(78, 23);
            this.lblInstallPath.TabIndex = 9;
            this.lblInstallPath.Text = "설치 경로";
            // 
            // btnSave
            // 
            this.btnSave.Font = new System.Drawing.Font("던파 비트비트체 v2", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.btnSave.Location = new System.Drawing.Point(154, 189);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(111, 41);
            this.btnSave.TabIndex = 10;
            this.btnSave.Text = "저장";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.btnSave_Click);
            // 
            // btnBrowsePath
            // 
            this.btnBrowsePath.Font = new System.Drawing.Font("던파 비트비트체 v2", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.btnBrowsePath.Location = new System.Drawing.Point(302, 23);
            this.btnBrowsePath.Name = "btnBrowsePath";
            this.btnBrowsePath.Size = new System.Drawing.Size(103, 30);
            this.btnBrowsePath.TabIndex = 11;
            this.btnBrowsePath.Text = "찾아보기";
            this.btnBrowsePath.UseVisualStyleBackColor = true;
            this.btnBrowsePath.Click += new System.EventHandler(this.btnBrowsePath_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("던파 비트비트체 v2", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.label1.Location = new System.Drawing.Point(82, 117);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(109, 23);
            this.label1.TabIndex = 12;
            this.label1.Text = "최대 램 (MB)";
            // 
            // ramValue
            // 
            this.ramValue.Font = new System.Drawing.Font("굴림", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.ramValue.Location = new System.Drawing.Point(222, 118);
            this.ramValue.Name = "ramValue";
            this.ramValue.Size = new System.Drawing.Size(116, 22);
            this.ramValue.TabIndex = 13;
            this.ramValue.Text = "2048";
            this.ramValue.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // version
            // 
            this.version.AutoSize = true;
            this.version.BackColor = System.Drawing.Color.Transparent;
            this.version.Font = new System.Drawing.Font("던파 비트비트체 v2", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.version.Location = new System.Drawing.Point(154, 158);
            this.version.Name = "version";
            this.version.Size = new System.Drawing.Size(0, 17);
            this.version.TabIndex = 14;
            this.version.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // SettingsForm
            // 
            this.ClientSize = new System.Drawing.Size(417, 247);
            this.Controls.Add(this.version);
            this.Controls.Add(this.ramValue);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.btnBrowsePath);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.lblInstallPath);
            this.Controls.Add(this.lblResolution);
            this.Controls.Add(this.txtInstallPath);
            this.Controls.Add(this.cboResolution);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "SettingsForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "설정";
            this.ResumeLayout(false);
            this.PerformLayout();

        }
        private System.Windows.Forms.ComboBox cboResolution;
        private System.Windows.Forms.TextBox txtInstallPath; // 설치 경로 TextBox 추가
        private System.Windows.Forms.Label lblResolution;
        private System.Windows.Forms.Label lblInstallPath; // 설치 경로 Label 추가
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnBrowsePath; // 설치 경로 탐색 버튼 추가
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox ramValue;
        private System.Windows.Forms.Label version;
    }
}

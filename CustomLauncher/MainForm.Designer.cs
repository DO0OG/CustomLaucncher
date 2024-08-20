using System;
using System.Windows.Forms;

namespace CustomLauncher
{
    partial class MainForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.progressBar1 = new System.Windows.Forms.ProgressBar();
            this.labelStatus = new System.Windows.Forms.Label();
            this.btnLogin = new System.Windows.Forms.Button();
            this.btnLogout = new System.Windows.Forms.Button();
            this.btnStartGame = new System.Windows.Forms.Button();
            this.EXIT = new System.Windows.Forms.Button();
            this.settingsBtn = new System.Windows.Forms.Button();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // progressBar1
            // 
            this.progressBar1.Location = new System.Drawing.Point(12, 537);
            this.progressBar1.Name = "progressBar1";
            this.progressBar1.Size = new System.Drawing.Size(379, 23);
            this.progressBar1.TabIndex = 4;
            // 
            // labelStatus
            // 
            this.labelStatus.AutoSize = true;
            this.labelStatus.BackColor = System.Drawing.Color.Transparent;
            this.labelStatus.Font = new System.Drawing.Font("던파 비트비트체 v2", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.labelStatus.Location = new System.Drawing.Point(157, 573);
            this.labelStatus.Name = "labelStatus";
            this.labelStatus.Size = new System.Drawing.Size(0, 17);
            this.labelStatus.TabIndex = 5;
            this.labelStatus.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // btnLogin
            // 
            this.btnLogin.BackColor = System.Drawing.Color.Transparent;
            this.btnLogin.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("btnLogin.BackgroundImage")));
            this.btnLogin.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnLogin.FlatAppearance.BorderSize = 0;
            this.btnLogin.FlatAppearance.MouseDownBackColor = System.Drawing.Color.Transparent;
            this.btnLogin.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            this.btnLogin.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLogin.Font = new System.Drawing.Font("던파 비트비트체 v2", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.btnLogin.ForeColor = System.Drawing.SystemColors.ActiveCaption;
            this.btnLogin.Location = new System.Drawing.Point(11, 601);
            this.btnLogin.Name = "btnLogin";
            this.btnLogin.Size = new System.Drawing.Size(381, 90);
            this.btnLogin.TabIndex = 0;
            this.btnLogin.Text = "로그인";
            this.btnLogin.UseVisualStyleBackColor = false;
            this.btnLogin.Click += new System.EventHandler(this.btnLogin_Click);
            this.btnLogin.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnLogin_MouseDown);
            this.btnLogin.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnLogin_MouseUp);
            // 
            // btnLogout
            // 
            this.btnLogout.BackColor = System.Drawing.Color.Transparent;
            this.btnLogout.BackgroundImage = global::CustomLauncher.Properties.Resources.Logout;
            this.btnLogout.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnLogout.FlatAppearance.BorderSize = 0;
            this.btnLogout.FlatAppearance.MouseDownBackColor = System.Drawing.Color.Transparent;
            this.btnLogout.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            this.btnLogout.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnLogout.Font = new System.Drawing.Font("던파 비트비트체 v2", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.btnLogout.ForeColor = System.Drawing.SystemColors.ActiveCaption;
            this.btnLogout.Location = new System.Drawing.Point(302, 601);
            this.btnLogout.Name = "btnLogout";
            this.btnLogout.Size = new System.Drawing.Size(90, 90);
            this.btnLogout.TabIndex = 2;
            this.btnLogout.Text = "로그아웃";
            this.btnLogout.UseVisualStyleBackColor = false;
            this.btnLogout.Click += new System.EventHandler(this.btnLogout_Click);
            this.btnLogout.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnLogout_MouseDown);
            this.btnLogout.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnLogout_MouseUp);
            // 
            // btnStartGame
            // 
            this.btnStartGame.BackColor = System.Drawing.Color.Transparent;
            this.btnStartGame.BackgroundImage = global::CustomLauncher.Properties.Resources.GameStart;
            this.btnStartGame.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.btnStartGame.FlatAppearance.BorderSize = 0;
            this.btnStartGame.FlatAppearance.MouseDownBackColor = System.Drawing.Color.Transparent;
            this.btnStartGame.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            this.btnStartGame.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStartGame.Font = new System.Drawing.Font("던파 비트비트체 v2", 24F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.btnStartGame.ForeColor = System.Drawing.SystemColors.ActiveCaption;
            this.btnStartGame.Location = new System.Drawing.Point(11, 601);
            this.btnStartGame.Margin = new System.Windows.Forms.Padding(15);
            this.btnStartGame.Name = "btnStartGame";
            this.btnStartGame.Size = new System.Drawing.Size(280, 90);
            this.btnStartGame.TabIndex = 1;
            this.btnStartGame.Text = "게임 시작";
            this.btnStartGame.UseVisualStyleBackColor = false;
            this.btnStartGame.Click += new System.EventHandler(this.btnStartGame_Click);
            this.btnStartGame.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnStartGame_MouseDown);
            this.btnStartGame.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnStartGame_MouseUp);
            // 
            // EXIT
            // 
            this.EXIT.BackColor = System.Drawing.Color.Transparent;
            this.EXIT.BackgroundImage = global::CustomLauncher.Properties.Resources.exit;
            this.EXIT.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.EXIT.FlatAppearance.BorderSize = 0;
            this.EXIT.FlatAppearance.MouseDownBackColor = System.Drawing.Color.Transparent;
            this.EXIT.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            this.EXIT.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.EXIT.Font = new System.Drawing.Font("던파 비트비트체 v2", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.EXIT.Location = new System.Drawing.Point(326, 14);
            this.EXIT.Name = "EXIT";
            this.EXIT.Size = new System.Drawing.Size(65, 65);
            this.EXIT.TabIndex = 4;
            this.EXIT.UseVisualStyleBackColor = false;
            this.EXIT.Click += new System.EventHandler(this.EXIT_Click);
            // 
            // settingsBtn
            // 
            this.settingsBtn.BackColor = System.Drawing.Color.Transparent;
            this.settingsBtn.BackgroundImage = global::CustomLauncher.Properties.Resources.setting;
            this.settingsBtn.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.settingsBtn.FlatAppearance.BorderSize = 0;
            this.settingsBtn.FlatAppearance.MouseDownBackColor = System.Drawing.Color.Transparent;
            this.settingsBtn.FlatAppearance.MouseOverBackColor = System.Drawing.Color.Transparent;
            this.settingsBtn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.settingsBtn.Font = new System.Drawing.Font("던파 비트비트체 v2", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.settingsBtn.Location = new System.Drawing.Point(11, 16);
            this.settingsBtn.Name = "settingsBtn";
            this.settingsBtn.Size = new System.Drawing.Size(63, 63);
            this.settingsBtn.TabIndex = 3;
            this.settingsBtn.UseVisualStyleBackColor = false;
            this.settingsBtn.Click += new System.EventHandler(this.btnSettings1_Click);
            this.settingsBtn.MouseDown += new System.Windows.Forms.MouseEventHandler(this.settingsBtn_MouseDown);
            this.settingsBtn.MouseUp += new System.Windows.Forms.MouseEventHandler(this.settingsBtn_MouseUp);
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackColor = System.Drawing.Color.Transparent;
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Center;
            this.pictureBox1.Image = global::CustomLauncher.Properties.Resources.logo;
            this.pictureBox1.Location = new System.Drawing.Point(12, 249);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(380, 190);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.StretchImage;
            this.pictureBox1.TabIndex = 2;
            this.pictureBox1.TabStop = false;
            this.pictureBox1.Click += new System.EventHandler(this.pictureBox1_Click);
            // 
            // MainForm
            // 
            this.BackgroundImage = global::CustomLauncher.Properties.Resources.BG;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            this.ClientSize = new System.Drawing.Size(404, 711);
            this.Controls.Add(this.btnLogin);
            this.Controls.Add(this.btnStartGame);
            this.Controls.Add(this.btnLogout);
            this.Controls.Add(this.EXIT);
            this.Controls.Add(this.settingsBtn);
            this.Controls.Add(this.labelStatus);
            this.Controls.Add(this.progressBar1);
            this.Controls.Add(this.pictureBox1);
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "DOG LAUNCHER";
            this.Shown += new System.EventHandler(this.MainForm_Shown);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        private void Txt_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                btnLogin.PerformClick();
            }
        }
        private PictureBox pictureBox1;
        private ProgressBar progressBar1;
        internal Label labelStatus;
        private Button btnLogin;
        private Button settingsBtn;
        private Button EXIT;
        private Button btnLogout;
        private Button btnStartGame;
    }
}

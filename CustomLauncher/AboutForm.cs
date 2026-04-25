using System;
using System.Drawing;
using System.Windows.Forms;

namespace CustomLauncher
{
    public class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
            FontLibrary.ApplyToControls(this);
        }

        private void InitializeComponent()
        {
            this.Text = "오픈소스 라이선스";
            this.Size = new Size(380, 300);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;

            var pnlContent = new Panel();
            pnlContent.Dock = DockStyle.Fill;
            pnlContent.Padding = new Padding(20, 16, 20, 0);

            var lblTitle = new Label();
            lblTitle.Text = "사용된 오픈소스 라이브러리";
            lblTitle.Font = new Font("맑은 고딕", 10F, FontStyle.Bold);
            lblTitle.Dock = DockStyle.Top;
            lblTitle.Height = 28;

            var lblList = new Label();
            lblList.Text =
                "CmlLib.Core  —  MIT License\r\n" +
                "CmlLib.Core.Auth.Microsoft  —  MIT License\r\n" +
                "CmlLib.Core.Installer.Forge  —  MIT License\r\n" +
                "Newtonsoft.Json  —  MIT License\r\n" +
                "NAudio  —  MIT License\r\n" +
                "RestSharp  —  Apache 2.0\r\n" +
                "SevenZipSharp  —  LGPL v3\r\n" +
                "HtmlAgilityPack  —  MIT License";
            lblList.Font = new Font("맑은 고딕", 9F);
            lblList.ForeColor = Color.FromArgb(60, 60, 60);
            lblList.Dock = DockStyle.Fill;
            lblList.AutoSize = false;

            var sep = new Panel();
            sep.Dock = DockStyle.Bottom;
            sep.Height = 1;
            sep.BackColor = Color.FromArgb(220, 220, 220);
            sep.Margin = new Padding(0, 8, 0, 0);

            pnlContent.Controls.Add(lblList);
            pnlContent.Controls.Add(lblTitle);

            var pnlFooter = new Panel();
            pnlFooter.Dock = DockStyle.Bottom;
            pnlFooter.Height = 52;
            pnlFooter.BackColor = Color.FromArgb(248, 248, 248);

            var btnClose = new Button();
            btnClose.Text = "닫기";
            btnClose.Size = new Size(80, 30);
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.FlatAppearance.BorderColor = Color.FromArgb(180, 180, 180);
            btnClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(230, 230, 230);
            btnClose.Font = new Font("맑은 고딕", 9F);
            btnClose.Click += (s, e) => this.Close();

            pnlFooter.Controls.Add(btnClose);
            pnlFooter.Resize += (s, e) =>
            {
                btnClose.Location = new Point(
                    (pnlFooter.Width - btnClose.Width) / 2,
                    (pnlFooter.Height - btnClose.Height) / 2
                );
            };

            this.Controls.Add(pnlContent);
            this.Controls.Add(sep);
            this.Controls.Add(pnlFooter);
        }
    }
}

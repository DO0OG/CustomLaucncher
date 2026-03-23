using System;
using System.Windows.Forms;
using System.Drawing;

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
            this.Text = "오픈소스 라이선스 정보";
            this.Size = new Size(500, 400);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            TextBox txtLicense = new TextBox();
            txtLicense.Multiline = true;
            txtLicense.ReadOnly = true;
            txtLicense.ScrollBars = ScrollBars.Vertical;
            txtLicense.Dock = DockStyle.Fill;
            txtLicense.Font = new Font("Consolas", 9F);
            
            txtLicense.Text = @"[Open Source Licenses]

1. CmlLib.Core
License: MIT License
Copyright (c) 2021-2024 CmlLib.Core Contributors
https://github.com/CmlLib/CmlLib.Core

2. Newtonsoft.Json
License: MIT License
Copyright (c) 2007 James Newton-King
https://github.com/JamesNK/Newtonsoft.Json

3. NAudio
License: MIT License
Copyright (c) 2020 Mark Heath
https://github.com/naudio/NAudio

4. SevenZipSharp
License: MIT License / LGPL
https://github.com/squid-box/SevenZipSharp

5. HtmlAgilityPack
License: MIT License
https://github.com/zzzprojects/html-agility-pack

--------------------------------------------------
MIT License Summary:
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the ""Software""), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
";

            this.Controls.Add(txtLicense);

            Button btnClose = new Button();
            btnClose.Text = "닫기";
            btnClose.Dock = DockStyle.Bottom;
            btnClose.Height = 40;
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }
    }
}

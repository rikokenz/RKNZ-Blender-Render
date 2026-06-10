using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace BlenderTool
{
    public class SettingsForm : Form
    {
        private RadioButton _lightRadio;
        private RadioButton _darkRadio;
        private TextBox     _pathTextBox;

        public SettingsForm()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            this.Text = "Preferences";
            this.Width = 520;
            this.Height = 220;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            // ── Theme label ───────────────────────────────────
            var themeLabel = new Label
            {
                Text = "Theme:",
                Left = 12,
                Top = 16,
                AutoSize = true
            };

            // ── Radio buttons ─────────────────────────────────
            _lightRadio = new RadioButton
            {
                Text    = "Light",
                Left    = 80,
                Top     = 13,
                Width   = 70,
                Checked = true
            };

            _darkRadio = new RadioButton
            {
                Text  = "Dark",
                Left  = 158,
                Top   = 13,
                Width = 70
            };

            // ── Separator ─────────────────────────────────────
            var sep = new Label
            {
                BorderStyle = BorderStyle.Fixed3D,
                Left        = 12,
                Top         = 40,
                Width       = 478,
                Height      = 2
            };

            // ── Blender path label ────────────────────────────
            var label = new Label
            {
                Text = "Blender Executable (blender.exe):",
                Left = 12,
                Top = 54,
                AutoSize = true
            };

            // ── Path text box ─────────────────────────────────
            _pathTextBox = new TextBox
            {
                Left = 12,
                Top = 74,
                Width = 390,
                PlaceholderText = "C:\\Program Files\\Blender Foundation\\Blender 4.x\\blender.exe"
            };

            // ── Browse button ─────────────────────────────────
            var browseBtn = new Button
            {
                Text = "Browse...",
                Left = 410,
                Top = 72,
                Width = 80
            };
            browseBtn.Click += BrowseBtn_Click;

            // ── Save button ───────────────────────────────────
            var saveBtn = new Button
            {
                Text = "Save",
                Left = 410,
                Top = 114,
                Width = 80
            };
            saveBtn.Click += SaveBtn_Click;

            // ── Cancel button ─────────────────────────────────
            var cancelBtn = new Button
            {
                Text = "Cancel",
                Left = 322,
                Top = 114,
                Width = 80
            };
            cancelBtn.Click += (s, e) => this.Close();

            this.Controls.AddRange(new Control[]
            {
                themeLabel, _lightRadio, _darkRadio, sep,
                label, _pathTextBox, browseBtn, saveBtn, cancelBtn
            });
        }

        private void BrowseBtn_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Select blender.exe",
                Filter = "Blender Executable|blender.exe|All Executables|*.exe"
            };

            if (dlg.ShowDialog() == DialogResult.OK)
                _pathTextBox.Text = dlg.FileName;
        }

        private void SaveBtn_Click(object sender, EventArgs e)
        {
            var path = _pathTextBox.Text.Trim();

            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
            {
                MessageBox.Show("The specified file does not exist. Please check the path.",
                    "Invalid Path", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var config = new AppConfig
            {
                BlenderPath = path,
                Theme       = _darkRadio.Checked ? "Dark" : "Light"
            };
            AppSettings.Save(config);

            // Apply theme immediately to the owner window and all open forms
            ThemeManager.Apply(config.Theme);

            MessageBox.Show("Preferences saved!", "Preferences", MessageBoxButtons.OK, MessageBoxIcon.Information);
            this.Close();
        }

        private void LoadSettings()
        {
            var config = AppSettings.Load();
            _pathTextBox.Text = config.BlenderPath ?? string.Empty;
            _darkRadio.Checked  = config.Theme == "Dark";
            _lightRadio.Checked = config.Theme != "Dark";
        }
    }
}


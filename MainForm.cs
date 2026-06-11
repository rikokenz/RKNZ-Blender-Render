using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BlenderTool
{
    public class MainForm : Form
    {
        // ── Add-job controls ──────────────────────────────────
        private TextBox _blendFileBox;
        private TextBox _frameStartBox;
        private TextBox _frameEndBox;
        private Button _browseBlendBtn;
        private Button _addToQueueBtn;

        // ── Queue list ────────────────────────────────────────
        private ListView _queueList;

        // ── Bottom bar ────────────────────────────────────────
        private Button _processBtn;
        private Button _stopBtn;
        private Button _showTerminalBtn;
        private ComboBox _whenDoneCombo;
        private ProgressBar _progressBar;
        private Label _progressLabel;
        private Label _totalEstLabel;
        private Button _sampleBtn;

        // ── Sampling CTS ──────────────────────────────────────
        private CancellationTokenSource? _sampleCts;

        // ── Terminal window ───────────────────────────────────
        private TerminalForm? _terminalForm;

        // ── Engine ────────────────────────────────────────────
        private readonly RenderQueue _queue = new();

        // ── Drag-reorder state ────────────────────────────────
        private int _dragFromIndex = -1;
        private Point _dragStartPoint;
        private bool _dragPending = false;

        public MainForm()
        {
            InitializeComponent();
            // Apply saved theme directly to this form (it isn't in Application.OpenForms
            // yet during construction, so ThemeManager.Apply would miss it).
            var _savedTheme = AppSettings.Load().Theme;
            ThemeManager.Apply(_savedTheme);          // sets IsDark + styles any already-open forms
            ThemeManager.ApplyToForm(this);           // explicitly style this form & its controls
            // Create the terminal once and keep it alive for the lifetime of the app.
            // It starts hidden; the Log button simply shows/hides it.
            _terminalForm = new TerminalForm();
            WireQueueEvents();
        }

        // ─────────────────────────────────────────────────────
        //  UI BUILD
        // ─────────────────────────────────────────────────────
        private void InitializeComponent()
        {
            this.Text = "RKNZ Blender Render";
            this.Width = 860;
            this.Height = 600;
            this.MinimumSize = new Size(700, 480);
            this.StartPosition = FormStartPosition.CenterScreen;

            // ── Menu bar ──────────────────────────────────────
            var menuStrip = new MenuStrip();
            var settingsMenu = new ToolStripMenuItem("Preferences");
            settingsMenu.Click += (s, e) =>
            {
                using var dlg = new SettingsForm();
                dlg.ShowDialog(this);
            };
            var aboutItem = new ToolStripMenuItem("About");
            aboutItem.Click += (s, e) =>
            {
                using var dlg = new Form
                {
                    Text = "About RKNZ Blender Render",
                    Width = 380,
                    Height = 420,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    StartPosition = FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false
                };

                var titleLabel = new Label
                {
                    Text = "RKNZ Blender Render",
                    Left = 20,
                    Top = 20,
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, 13f, FontStyle.Bold)
                };

                var versionLabel = new Label
                {
                    Text = "Version : 1.0.0",
                    Left = 20,
                    Top = 50,
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, 10f)
                };

                var devLabel = new Label
                {
                    Text = "Developed by Rikokenz",
                    Left = 20,
                    Top = 74,
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, 10f)
                };

                var githubLink = new LinkLabel
                {
                    Text = "github.com/rikokenz",
                    Left = 20,
                    Top = 98,
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, 9f)
                };
                githubLink.LinkClicked += (ls, le) =>
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://github.com/rikokenz",
                        UseShellExecute = true
                    });
                };

                // separator before support
                var separator = new Label
                {
                    Text = new string('─', 46),
                    Left = 20,
                    Top = 124,
                    AutoSize = true,
                    ForeColor = Color.LightGray,
                    Font = new Font(Font.FontFamily, 8f)
                };

                var supportLabel = new Label
                {
                    Text = "☕  Support the Developer",
                    Left = 20,
                    Top = 142,
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
                };

                var supportDesc = new Label
                {
                    Text = "If you find this tool useful, consider buying me a coffee!",
                    Left = 20,
                    Top = 162,
                    Width = 330,
                    AutoSize = true,
                    ForeColor = Color.Gray,
                    Font = new Font(Font.FontFamily, 8f)
                };

                var patreonLink = new LinkLabel
                {
                    Text = "Support me on Patreon →",
                    Left = 20,
                    Top = 180,
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, 9f)
                };
                patreonLink.LinkClicked += (ls, le) =>
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://www.patreon.com/rikokensfw",
                        UseShellExecute = true
                    });
                };

                // separator before license
                var separator2 = new Label
                {
                    Text = new string('─', 46),
                    Left = 20,
                    Top = 206,
                    AutoSize = true,
                    ForeColor = Color.LightGray,
                    Font = new Font(Font.FontFamily, 8f)
                };

                var licenseTitle = new Label
                {
                    Text = "License: GNU General Public License v3.0",
                    Left = 20,
                    Top = 224,
                    AutoSize = true,
                    Font = new Font(Font.FontFamily, 9f, FontStyle.Bold)
                };

                var licenseDesc = new Label
                {
                    Text = "Free to use, modify, and distribute. Any modified\n" +
                           "version must also be open-source under GPL v3.\n" +
                           "No warranty is provided.",
                    Left = 20,
                    Top = 244,
                    Width = 330,
                    AutoSize = true,
                    ForeColor = Color.Gray,
                    Font = new Font(Font.FontFamily, 8f)
                };

                var copyrightLabel = new Label
                {
                    Text = "© 2026 Rikokenz Studio. All rights reserved.",
                    Left = 20,
                    Top = 302,
                    AutoSize = true,
                    ForeColor = Color.Gray,
                    Font = new Font(Font.FontFamily, 8f)
                };

                var closeBtn = new Button
                {
                    Text = "Close",
                    Left = 140,
                    Top = 332,
                    Width = 90,
                    DialogResult = DialogResult.OK
                };

                dlg.AcceptButton = closeBtn;
                dlg.Controls.AddRange(new Control[]
                {
                    titleLabel, versionLabel, devLabel, githubLink,
                    separator, supportLabel, supportDesc, patreonLink,
                    separator2, licenseTitle, licenseDesc,
                    copyrightLabel, closeBtn
                });
                dlg.ShowDialog(this);
            };

            menuStrip.Items.Add(settingsMenu);

            var helpMenu = new ToolStripMenuItem("Help");

            var checkUpdateItem = new ToolStripMenuItem("Check for Update");
            checkUpdateItem.Click += (s, e) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/rikokenz/RKNZ-Blender-Render",
                    UseShellExecute = true
                });
            };

            var reportBugItem = new ToolStripMenuItem("Report Bug");
            reportBugItem.Click += (s, e) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/rikokenz/RKNZ-Blender-Render/issues",
                    UseShellExecute = true
                });
            };

            helpMenu.DropDownItems.Add(checkUpdateItem);
            helpMenu.DropDownItems.Add(reportBugItem);
            menuStrip.Items.Add(helpMenu);

            menuStrip.Items.Add(aboutItem);
            this.MainMenuStrip = menuStrip;
            this.Controls.Add(menuStrip);

            // ── Top panel – add job ───────────────────────────
            var topPanel = new Panel
            {
                Left = 0,
                Top = 28,
                Width = this.ClientSize.Width,
                Height = 110,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Padding = new Padding(10)
            };
            topPanel.Paint += (s, e) =>
            {
                e.Graphics.DrawLine(Pens.LightGray, 0, topPanel.Height - 1,
                                    topPanel.Width, topPanel.Height - 1);
            };

            // Row 1 – blend file
            var blendLabel = new Label { Text = "Blend File:", Left = 10, Top = 14, AutoSize = true };

            _blendFileBox = new TextBox
            {
                Left = 90,
                Top = 11,
                Width = topPanel.Width - 220,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                PlaceholderText = "C:\\Projects\\scene.blend"
            };

            _browseBlendBtn = new Button
            {
                Text = "Browse…",
                Left = topPanel.Width - 120,
                Top = 10,
                Width = 100,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _browseBlendBtn.Click += BrowseBlend_Click;

            // Row 2 – frame range
            var frameLabel = new Label { Text = "Frame Range:", Left = 10, Top = 46, AutoSize = true };
            var startLabel = new Label { Text = "Start:", Left = 90, Top = 46, AutoSize = true };

            _frameStartBox = new TextBox
            {
                Left = 130,
                Top = 43,
                Width = 60,
                PlaceholderText = "1"
            };

            var endLabel = new Label { Text = "End:", Left = 200, Top = 46, AutoSize = true };

            _frameEndBox = new TextBox
            {
                Left = 230,
                Top = 43,
                Width = 60,
                PlaceholderText = "250"
            };

            var rangeHint = new Label
            {
                Text = "(leave empty to render full timeline)",
                Left = 300,
                Top = 46,
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 8f)
            };

            // Row 3 – add button
            _addToQueueBtn = new Button
            {
                Text = "+ Add to Queue",
                Left = 10,
                Top = 74,
                Width = 130,
                Height = 28
            };
            _addToQueueBtn.Click += AddToQueue_Click;

            topPanel.Controls.AddRange(new Control[]
            {
                blendLabel, _blendFileBox, _browseBlendBtn,
                frameLabel, startLabel, _frameStartBox,
                endLabel,   _frameEndBox,  rangeHint,
                _addToQueueBtn
            });
            this.Controls.Add(topPanel);

            // ── Queue list view ───────────────────────────────
            _queueList = new ListView
            {
                Left = 10,
                Top = 148,
                Width = this.ClientSize.Width - 20,
                Height = this.ClientSize.Height - 148 - 110,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                          | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                AllowDrop = true
            };
            _queueList.Columns.Add("#", 30);
            _queueList.Columns.Add("Blend File", 240);
            _queueList.Columns.Add("Frame Range", 100);
            _queueList.Columns.Add("Output", 180);
            _queueList.Columns.Add("Est. Time", 130);
            _queueList.Columns.Add("Status", 90);

            // Double-click to edit
            _queueList.MouseDoubleClick += QueueList_MouseDoubleClick;

            // Drag-to-reorder (deferred so double-click is not eaten)
            _queueList.MouseDown += QueueList_MouseDown;
            _queueList.MouseMove += QueueList_MouseMove;
            _queueList.MouseUp += QueueList_MouseUp;
            _queueList.DragOver += QueueList_DragOver;
            _queueList.DragDrop += QueueList_DragDrop;

            // Right-click context menu
            var ctx = new ContextMenuStrip();
            var removeItem = new ToolStripMenuItem("Remove");
            removeItem.Click += RemoveSelected_Click;
            var resetItem = new ToolStripMenuItem("Reset to Waiting");
            resetItem.Click += (s, e) =>
            {
                foreach (ListViewItem item in _queueList.SelectedItems)
                {
                    int idx = item.Index;
                    if (idx < 0 || idx >= _queue.Jobs.Count) continue;
                    var j = _queue.Jobs[idx];
                    j.Status = JobStatus.Waiting;
                }
                RefreshList();
            };
            var openOutputItem = new ToolStripMenuItem("Open Output in Explorer");
            openOutputItem.Click += OpenOutputInExplorer_Click;
            ctx.Items.Add(resetItem);
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add(openOutputItem);
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add(removeItem);
            ctx.Opening += (s, e) =>
            {
                var sel = _queueList.SelectedItems;
                if (sel.Count == 0) { e.Cancel = true; return; }
                int idx = sel[0].Index;
                var job = idx < _queue.Jobs.Count ? _queue.Jobs[idx] : null;
                // Reset only makes sense for Done/Failed jobs
                resetItem.Enabled = job != null &&
                    (job.Status == JobStatus.Done || job.Status == JobStatus.Failed);
                // Always enabled as long as there's a job selected — default falls back to blend dir
                openOutputItem.Enabled = job != null && File.Exists(job.BlendFile);
            };
            _queueList.ContextMenuStrip = ctx;

            this.Controls.Add(_queueList);

            // ── Bottom bar ────────────────────────────────────
            // Uses a TableLayoutPanel so every control is aligned in a proper grid.
            // Layout (2 columns: left = progress/estimate, right = when-done + action buttons):
            //
            //  ┌─────────────────────────────────┬──────────────────────────────────┐
            //  │ ProgressBar (fills width)        │ [When done: ▼ dropdown         ] │
            //  ├─────────────────────────────────┤                                  │
            //  │ Progress status label            │ [▶ Process Queue][■ Stop][📋Log] │
            //  ├─────────────────────────────────┤                                  │
            //  │ Total Est.: —  [⏱ Calc. Est.]   │                                  │
            //  └─────────────────────────────────┴──────────────────────────────────┘

            var bottomPanel = new Panel
            {
                Left = 0,
                Top = this.ClientSize.Height - 110,
                Width = this.ClientSize.Width,
                Height = 110,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Padding = new Padding(8, 6, 8, 6)
            };

            // ── Left column: progress bar, status label, estimate row ──
            _progressBar = new ProgressBar
            {
                Height = 20,
                Width = 10,   // placeholder; SizeChanged will set real width
                Margin = new Padding(0, 0, 0, 4)
            };

            _progressLabel = new Label
            {
                Text = "Ready",
                Height = 18,
                Width = 10,   // placeholder; SizeChanged will set real width
                AutoSize = false,
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // Estimate row: label + Calc button side-by-side
            _totalEstLabel = new Label
            {
                Text = "Total Est.: —",
                AutoSize = false,
                ForeColor = Color.DimGray,
                Font = new Font(Font.FontFamily, 8f),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _sampleBtn = new Button
            {
                Text = "⏱ Calc. Est.",
                Width = 110,
                Height = 22,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font.FontFamily, 8f)
            };
            _sampleBtn.Click += SampleBtn_Click;

            // Estimate row panel holds the label and button in a single cell.
            // Explicit Height required — FlowLayoutPanel ignores Dock on children.
            var estRowPanel = new Panel { Height = 26 };
            estRowPanel.Controls.Add(_totalEstLabel);
            estRowPanel.Controls.Add(_sampleBtn);
            _totalEstLabel.Left   = 0;
            _totalEstLabel.Top    = 2;
            _totalEstLabel.Height = 22;
            _sampleBtn.Top        = 2;

            // Left stack panel using FlowLayoutPanel (vertical)
            var leftStack = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                Dock = DockStyle.Fill,
                WrapContents = false,
                AutoSize = false
            };

            leftStack.Controls.Add(_progressBar);
            leftStack.Controls.Add(_progressLabel);
            leftStack.Controls.Add(estRowPanel);

            // FlowLayoutPanel ignores Dock on children; use Layout event so the width
            // is applied both on first layout pass and on every subsequent resize.
            leftStack.Layout += (s, e) =>
            {
                if (leftStack.ClientSize.Width > 0)
                {
                    int w = leftStack.ClientSize.Width;
                    _progressBar.Width   = w;
                    _progressLabel.Width = w;
                    estRowPanel.Width    = w;
                    _totalEstLabel.Width = w - _sampleBtn.Width - 12;
                    _sampleBtn.Left      = w - _sampleBtn.Width - 8;
                }
            };

            // ── Right column: When done label+combo, then action buttons ──
            var whenDoneLabel = new Label
            {
                Text = "When done:",
                AutoSize = false,
                Width = 76,
                Height = 22,
                ForeColor = Color.DimGray,
                Font = new Font(Font.FontFamily, 7.5f),
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.None
            };

            _whenDoneCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font(Font.FontFamily, 8f),
                Height = 22,
                Dock = DockStyle.None
            };
            _whenDoneCombo.Items.AddRange(new object[]
            {
                "Do nothing",
                "Hibernate",
                "Shutdown",
                "Support Me  ^^"
            });
            _whenDoneCombo.SelectedIndex = 0;

            // When-done row: label + combo side-by-side
            var whenDoneRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 4)
            };
            whenDoneRow.Controls.Add(whenDoneLabel);
            whenDoneRow.Controls.Add(_whenDoneCombo);

            _processBtn = new Button
            {
                Text = "▶  Process Queue",
                Width = 150,
                Height = 38,
                BackColor = Color.FromArgb(0, 120, 212),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _processBtn.Click += ProcessQueue_Click;

            _stopBtn = new Button
            {
                Text = "■  Stop",
                Width = 80,
                Height = 38,
                Enabled = false,
                FlatStyle = FlatStyle.Flat
            };
            _stopBtn.Click += (s, e) => _queue.Stop();

            _showTerminalBtn = new Button
            {
                Text = "📋 Log",
                Width = 58,
                Height = 38,
                FlatStyle = FlatStyle.Flat
            };
            _showTerminalBtn.Click += ShowTerminal_Click;

            // Action buttons row
            var actionRow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Dock = DockStyle.Top
            };
            actionRow.Controls.Add(_processBtn);
            actionRow.Controls.Add(_stopBtn);
            actionRow.Controls.Add(_showTerminalBtn);

            // Right stack panel
            var rightStack = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 0, 0, 0)
            };
            rightStack.Controls.Add(actionRow);
            rightStack.Controls.Add(whenDoneRow);
            // Stack them vertically via Top offset; whenDoneRow on top, actionRow below
            whenDoneRow.Dock = DockStyle.None;
            actionRow.Dock = DockStyle.None;
            whenDoneRow.Top = 2;
            whenDoneRow.Left = 8;
            actionRow.Top = 32;
            actionRow.Left = 8;

            // size the combo to fill remaining space in its row once the panel is sized
            rightStack.SizeChanged += (s, e) =>
            {
                int comboW = rightStack.ClientSize.Width - 8 - whenDoneLabel.Width - whenDoneRow.Margin.Horizontal - 4;
                if (comboW > 60) _whenDoneCombo.Width = comboW;
            };

            // Main table: 2 columns (left fills, right is fixed 310px)
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 318f));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            table.Controls.Add(leftStack, 0, 0);
            table.Controls.Add(rightStack, 1, 0);

            bottomPanel.Controls.Add(table);
            this.Controls.Add(bottomPanel);
        }

        // ─────────────────────────────────────────────────────
        //  QUEUE EVENTS
        // ─────────────────────────────────────────────────────
        private void WireQueueEvents()
        {
            _queue.ProgressChanged += (done, total, msg) =>
            {
                this.Invoke(() =>
                {
                    // msg-only update (output lines): done == -1
                    if (done >= 0 && total > 0)
                    {
                        _progressBar.Maximum = total;
                        _progressBar.Value = done;
                    }
                    _progressLabel.Text = msg;
                    _terminalForm?.AppendLine(msg);
                    RefreshList();
                });
            };

            _queue.EstimateUpdated += () =>
            {
                this.Invoke(() =>
                {
                    _queue.PropagateEstimates();
                    RefreshList();
                });
            };

            _queue.QueueFinished += () =>
            {
                this.Invoke(() =>
                {
                    _processBtn.Enabled = true;
                    _stopBtn.Enabled = false;
                    _sampleBtn.Enabled = true;
                    _progressLabel.Text = "Queue finished.";
                    _queue.PropagateEstimates();
                    RefreshList();

                    var selected = _whenDoneCombo.SelectedItem?.ToString() ?? "Do nothing";
                    switch (selected)
                    {
                        case "Hibernate":
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "shutdown",
                                Arguments = "/h",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            });
                            break;

                        case "Shutdown":
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "shutdown",
                                Arguments = "/s /t 0",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            });
                            break;

                        case "Support Me  ^^":
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "https://www.patreon.com/rikokensfw",
                                UseShellExecute = true
                            });
                            break;

                        // "Do nothing" — fall through
                    }
                });
            };
        }

        private async void SampleBtn_Click(object? sender, EventArgs e)
        {
            if (_queue.Jobs.Count == 0)
            {
                MessageBox.Show("Add at least one job to the queue first.",
                    "Empty Queue", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(AppSettings.GetBlenderPath()))
            {
                MessageBox.Show("Please set the path to blender.exe in Settings first.",
                    "Blender Not Set", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _sampleBtn.Enabled  = false;
            _processBtn.Enabled = false;
            _sampleCts = new CancellationTokenSource();
            _progressLabel.Text = "Sampling frames for time estimate…";

            try
            {
                await _queue.SampleAllAsync(_sampleCts.Token);
                _queue.PropagateEstimates();
                RefreshList();
                _progressLabel.Text = "Estimation complete.";
            }
            finally
            {
                _sampleCts = null;
                _sampleBtn.Enabled  = true;
                _processBtn.Enabled = true;
            }
        }

        // ─────────────────────────────────────────────────────
        //  DOUBLE-CLICK EDITING
        // ─────────────────────────────────────────────────────
        private void QueueList_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            // Cancel any pending drag so it doesn't fire after the dialog closes
            _dragPending = false;
            _dragFromIndex = -1;

            var hitTest = _queueList.HitTest(e.Location);
            if (hitTest.Item == null) return;

            int jobIndex = hitTest.Item.Index;
            if (jobIndex < 0 || jobIndex >= _queue.Jobs.Count) return;

            // Guard: don't allow editing a job that is already rendering or done
            var job = _queue.Jobs[jobIndex];
            if (job.Status == JobStatus.Rendering || job.Status == JobStatus.Done) return;

            // Use X position to detect column — more reliable than SubItems.IndexOf
            int colIndex = GetColumnAtX(e.X);

            if (colIndex == 2)
                EditFrameRange(job);
            else if (colIndex == 3)
                EditOutputPath(job);
            // All other columns (blend file, est. time, status, #) — do nothing on double-click
        }

        // Returns which column index the given X coordinate falls in
        private int GetColumnAtX(int x)
        {
            int offset = 0;
            for (int i = 0; i < _queueList.Columns.Count; i++)
            {
                offset += _queueList.Columns[i].Width;
                if (x < offset) return i;
            }
            return -1;
        }

        private void EditBlendFile(RenderJob job)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Select .blend file",
                Filter = "Blend Files|*.blend|All Files|*.*",
                FileName = job.BlendFile
            };

            if (File.Exists(job.BlendFile))
                dlg.InitialDirectory = Path.GetDirectoryName(job.BlendFile);

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                job.BlendFile = dlg.FileName;
                RefreshList();
            }
        }

        private void EditFrameRange(RenderJob job)
        {
            // Small dialog: Start / End text boxes + OK / Cancel
            using var dlg = new Form
            {
                Text = "Edit Frame Range",
                Width = 280,
                Height = 140,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var startLbl = new Label { Text = "Start:", Left = 12, Top = 16, AutoSize = true };
            var startTxt = new TextBox
            {
                Left = 60,
                Top = 13,
                Width = 70,
                Text = job.FrameStart,
                PlaceholderText = "1"
            };

            var endLbl = new Label { Text = "End:", Left = 148, Top = 16, AutoSize = true };
            var endTxt = new TextBox
            {
                Left = 180,
                Top = 13,
                Width = 70,
                Text = job.FrameEnd,
                PlaceholderText = "250"
            };

            var hint = new Label
            {
                Text = "Leave both empty for full timeline.",
                Left = 12,
                Top = 46,
                Width = 240,
                AutoSize = false,
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 8f)
            };

            var okBtn = new Button
            {
                Text = "OK",
                Left = 100,
                Top = 70,
                Width = 70,
                DialogResult = DialogResult.OK
            };
            var cancelBtn = new Button
            {
                Text = "Cancel",
                Left = 178,
                Top = 70,
                Width = 70,
                DialogResult = DialogResult.Cancel
            };

            dlg.AcceptButton = okBtn;
            dlg.CancelButton = cancelBtn;
            dlg.Controls.AddRange(new Control[]
                { startLbl, startTxt, endLbl, endTxt, hint, okBtn, cancelBtn });

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                job.FrameStart = startTxt.Text.Trim();
                job.FrameEnd = endTxt.Text.Trim();
                RefreshList();
            }
        }

        private void EditOutputPath(RenderJob job)
        {
            // Small dialog with a text box + Browse button for the output directory
            using var dlg = new Form
            {
                Text = "Set Output Directory",
                Width = 480,
                Height = 130,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };

            var lbl = new Label { Text = "Output folder:", Left = 12, Top = 16, AutoSize = true };
            var txt = new TextBox
            {
                Left = 110,
                Top = 13,
                Width = 240,
                Text = job.OutputPath,
                PlaceholderText = @"C:\renders\project\"
            };
            var browseBtn = new Button { Text = "Browse…", Left = 358, Top = 12, Width = 80 };
            browseBtn.Click += (s, e) =>
            {
                using var fbd = new FolderBrowserDialog
                {
                    Description = "Select output directory",
                    SelectedPath = Directory.Exists(txt.Text) ? txt.Text : string.Empty
                };
                if (fbd.ShowDialog() == DialogResult.OK)
                    txt.Text = fbd.SelectedPath;
            };

            var hint = new Label
            {
                Text = "Leave empty to use the output path set in the .blend file.",
                Left = 12,
                Top = 46,
                Width = 440,
                AutoSize = false,
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 8f)
            };

            var okBtn = new Button
            {
                Text = "OK",
                Left = 286,
                Top = 68,
                Width = 70,
                DialogResult = DialogResult.OK
            };
            var cancelBtn = new Button
            {
                Text = "Cancel",
                Left = 364,
                Top = 68,
                Width = 80,
                DialogResult = DialogResult.Cancel
            };

            dlg.AcceptButton = okBtn;
            dlg.CancelButton = cancelBtn;
            dlg.Controls.AddRange(new Control[] { lbl, txt, browseBtn, hint, okBtn, cancelBtn });

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                job.OutputPath = txt.Text.Trim();
                RefreshList();
            }
        }

        private void OpenOutputInExplorer_Click(object? sender, EventArgs e)
        {
            var sel = _queueList.SelectedItems;
            if (sel.Count == 0) return;
            int idx = sel[0].Index;
            if (idx < 0 || idx >= _queue.Jobs.Count) return;

            var job = _queue.Jobs[idx];

            // Use the explicit output path if set, otherwise fall back to the .blend file's directory
            var path = !string.IsNullOrWhiteSpace(job.OutputPath)
                ? job.OutputPath
                : Path.GetDirectoryName(job.BlendFile) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("Cannot determine output directory.",
                    "No Path", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!Directory.Exists(path))
            {
                var result = MessageBox.Show(
                    $"The directory does not exist yet:\n{path}\n\nCreate it now?",
                    "Directory Not Found", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.Yes)
                {
                    try { Directory.CreateDirectory(path); }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not create directory:\n{ex.Message}",
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                else return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }

        // ─────────────────────────────────────────────────────
        //  DRAG-TO-REORDER
        // ─────────────────────────────────────────────────────
        private void QueueList_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_queue.IsRunning) return;

            var hit = _queueList.HitTest(e.Location);
            if (hit.Item == null) { _dragFromIndex = -1; return; }

            // Record intent, but wait for MouseMove before starting DoDragDrop.
            // This prevents DoDragDrop from swallowing the second click of a double-click.
            _dragFromIndex = hit.Item.Index;
            _dragStartPoint = e.Location;
            _dragPending = true;
        }

        private void QueueList_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!_dragPending || _dragFromIndex < 0) return;

            // Only begin the drag once the cursor has moved past the system drag threshold
            if (Math.Abs(e.X - _dragStartPoint.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - _dragStartPoint.Y) < SystemInformation.DragSize.Height)
                return;

            _dragPending = false;
            _queueList.DoDragDrop(_dragFromIndex, DragDropEffects.Move);
        }

        private void QueueList_MouseUp(object? sender, MouseEventArgs e)
        {
            // If the mouse was released without dragging, cancel the pending drag
            _dragPending = false;
        }

        private void QueueList_DragOver(object? sender, DragEventArgs e)
        {
            if (_dragFromIndex < 0) return;
            e.Effect = DragDropEffects.Move;

            // Draw an insertion-line cursor so the user can see where the row will land
            var pt = _queueList.PointToClient(new Point(e.X, e.Y));
            var hit = _queueList.HitTest(pt);
            _queueList.Cursor = hit.Item != null ? Cursors.Hand : Cursors.No;
        }

        private void QueueList_DragDrop(object? sender, DragEventArgs e)
        {
            _queueList.Cursor = Cursors.Default;
            if (_dragFromIndex < 0) return;

            var pt = _queueList.PointToClient(new Point(e.X, e.Y));
            var hit = _queueList.HitTest(pt);
            int toIndex = hit.Item?.Index ?? _queue.Jobs.Count - 1;

            if (toIndex == _dragFromIndex) { _dragFromIndex = -1; return; }

            var job = _queue.Jobs[_dragFromIndex];
            _queue.Jobs.RemoveAt(_dragFromIndex);
            _queue.Jobs.Insert(toIndex, job);

            _dragFromIndex = -1;
            RefreshList();

            // Re-select the moved row so the user can see where it landed
            if (toIndex < _queueList.Items.Count)
                _queueList.Items[toIndex].Selected = true;
        }

        // ─────────────────────────────────────────────────────
        //  HANDLERS
        // ─────────────────────────────────────────────────────
        private void BrowseBlend_Click(object? sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Select .blend file",
                Filter = "Blend Files|*.blend|All Files|*.*"
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                _blendFileBox.Text = dlg.FileName;
        }

        private void AddToQueue_Click(object? sender, EventArgs e)
        {
            var file = _blendFileBox.Text.Trim();
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                MessageBox.Show("Please select a valid .blend file.",
                    "Missing File", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var job = new RenderJob
            {
                BlendFile = file,
                FrameStart = _frameStartBox.Text.Trim(),
                FrameEnd = _frameEndBox.Text.Trim()
            };
            _queue.Jobs.Add(job);
            // If a sampled estimate exists for this blend file, copy it to the new job immediately
            // so the Est. Time column shows up without needing to re-run Calc. Est.
            _queue.PropagateEstimates();
            RefreshList();
        }

        private void RemoveSelected_Click(object? sender, EventArgs e)
        {
            foreach (ListViewItem item in _queueList.SelectedItems)
            {
                int idx = item.Index;
                if (idx < _queue.Jobs.Count)
                    _queue.Jobs.RemoveAt(idx);
            }
            RefreshList();
        }

        private async void ProcessQueue_Click(object? sender, EventArgs e)
        {
            if (_queue.Jobs.Count == 0)
            {
                MessageBox.Show("Add at least one job to the queue first.",
                    "Empty Queue", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(AppSettings.GetBlenderPath()))
            {
                MessageBox.Show("Please set the path to blender.exe in Settings first.",
                    "Blender Not Set", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _processBtn.Enabled = false;
            _stopBtn.Enabled = true;
            _progressBar.Value = 0;
            _progressBar.Maximum = _queue.Jobs.Count;

            await _queue.StartAsync();
        }

        private void ShowTerminal_Click(object? sender, EventArgs e)
        {
            // The terminal is always alive; just toggle visibility.
            if (_terminalForm!.Visible)
            {
                _terminalForm.Hide();
            }
            else
            {
                _terminalForm.Show(this);
                _terminalForm.BringToFront();
            }
        }

        // ─────────────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────────────
        private void RefreshList()
        {
            _queueList.Items.Clear();

            // Force the ListView's own ForeColor to the correct default so unselected
            // rows don't appear grey — ThemeManager may set this to a system color that
            // renders grey in some Windows visual styles.
            _queueList.ForeColor = ThemeManager.IsDark
                ? Color.FromArgb(220, 220, 220)
                : Color.Black;

            for (int i = 0; i < _queue.Jobs.Count; i++)
            {
                var job = _queue.Jobs[i];
                var item = new ListViewItem((i + 1).ToString());
                item.UseItemStyleForSubItems = true;
                item.SubItems.Add(job.BlendFile);
                item.SubItems.Add(job.FrameRangeDisplay);
                item.SubItems.Add(job.OutputDisplay);
                item.SubItems.Add(job.EstimatedTimeDisplay);
                item.SubItems.Add(job.StatusText);

                item.ForeColor = job.Status switch
                {
                    JobStatus.Done      => Color.Green,
                    JobStatus.Failed    => Color.Red,
                    JobStatus.Rendering => Color.DodgerBlue,
                    _ => ThemeManager.IsDark ? Color.FromArgb(220, 220, 220) : Color.Black
                };
                _queueList.Items.Add(item);
            }

            UpdateTotalEstimate();
        }

        private void UpdateTotalEstimate()
        {
            double total = 0;
            bool anyUnknown = false;
            foreach (var job in _queue.Jobs)
            {
                if (job.Status == JobStatus.Done) continue;
                if (job.SecondsPerFrame == null) { anyUnknown = true; continue; }
                int? fc = job.FrameCount;
                if (fc == null) { anyUnknown = true; continue; }
                total += job.SecondsPerFrame.Value * fc.Value;
            }

            if (total == 0 && anyUnknown)
                _totalEstLabel.Text = "Total Est.: —";
            else if (anyUnknown)
                _totalEstLabel.Text = $"Total Est.: {RenderJob.FormatSeconds(total)} + unknown";
            else if (total == 0)
                _totalEstLabel.Text = "Total Est.: —";
            else
                _totalEstLabel.Text = $"Total Est.: {RenderJob.FormatSeconds(total)}";
        }
    }
}
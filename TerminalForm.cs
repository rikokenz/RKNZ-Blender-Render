using System;
using System.Drawing;
using System.Windows.Forms;

namespace BlenderTool
{
    public class TerminalForm : Form
    {
        private RichTextBox _output;
        private Button      _clearBtn;

        public TerminalForm()
        {
            this.Text            = "Render Terminal Output";
            this.Width           = 700;
            this.Height          = 400;
            this.StartPosition   = FormStartPosition.Manual;
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;

            // Position it to the right of / below the caller
            this.Location = new Point(
                Screen.PrimaryScreen!.WorkingArea.Right - this.Width - 20,
                Screen.PrimaryScreen!.WorkingArea.Bottom - this.Height - 60);

            // Closing the window hides it instead of destroying it,
            // so the log is preserved across open/close cycles.
            // We only intercept user-initiated closes (CloseReason.UserClosing);
            // any other reason (app shutdown, task kill, etc.) is let through so
            // the process can actually exit.
            this.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    this.Hide();
                }
            };

            _output = new RichTextBox
            {
                Dock       = DockStyle.Fill,
                BackColor  = Color.Black,
                ForeColor  = Color.LimeGreen,
                Font       = new Font("Consolas", 9f),
                ReadOnly   = true,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            _clearBtn = new Button
            {
                Text   = "Clear",
                Dock   = DockStyle.Bottom,
                Height = 28
            };
            _clearBtn.Click += (s, e) => _output.Clear();

            this.Controls.Add(_output);
            this.Controls.Add(_clearBtn);
        }

        public void AppendLine(string line)
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.Invoke(() => AppendLine(line));
                return;
            }
            _output.AppendText(line + Environment.NewLine);
            _output.ScrollToCaret();
        }
    }
}

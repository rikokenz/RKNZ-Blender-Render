using System.Drawing;
using System.Windows.Forms;

namespace BlenderTool
{
    public static class ThemeManager
    {
        // ── Palettes ──────────────────────────────────────────
        private static readonly Color DarkBg      = Color.FromArgb(30,  30,  30);
        private static readonly Color DarkSurface = Color.FromArgb(45,  45,  48);
        private static readonly Color DarkFg      = Color.FromArgb(220, 220, 220);
        private static readonly Color DarkBorder  = Color.FromArgb(70,  70,  70);

        private static readonly Color LightBg      = SystemColors.Control;
        private static readonly Color LightSurface = SystemColors.Window;
        private static readonly Color LightFg      = SystemColors.ControlText;

        public static bool IsDark { get; private set; }

        /// <summary>Apply the chosen theme to every open form in the application.</summary>
        public static void Apply(string theme)
        {
            IsDark = theme == "Dark";

            foreach (Form f in Application.OpenForms)
                ApplyToForm(f);
        }

        public static void ApplyToForm(Form form)
        {
            if (IsDark)
            {
                form.BackColor = DarkBg;
                form.ForeColor = DarkFg;
            }
            else
            {
                form.BackColor = LightBg;
                form.ForeColor = LightFg;
            }

            ApplyToControls(form.Controls);
        }

        // ── Recursive control walker ──────────────────────────
        private static void ApplyToControls(Control.ControlCollection controls)
        {
            foreach (Control c in controls)
            {
                StyleControl(c);
                if (c.HasChildren)
                    ApplyToControls(c.Controls);
            }
        }

        private static void StyleControl(Control c)
        {
            if (IsDark)
            {
                switch (c)
                {
                    case MenuStrip ms:
                        ms.BackColor = DarkSurface;
                        ms.ForeColor = DarkFg;
                        StyleMenuItems(ms.Items);
                        break;

                    case ToolStrip ts:
                        ts.BackColor = DarkSurface;
                        ts.ForeColor = DarkFg;
                        break;

                    case ListView lv:
                        lv.BackColor = DarkSurface;
                        lv.ForeColor = DarkFg;
                        break;

                    case TextBox tb:
                        tb.BackColor = DarkSurface;
                        tb.ForeColor = DarkFg;
                        break;

                    case RichTextBox rtb:
                        // Terminal keeps its own black/green — don't override
                        break;

                    case Button btn when btn.BackColor == Color.FromArgb(0, 120, 212):
                        // Keep the accent process button as-is
                        break;

                    case Button btn:
                        btn.BackColor = DarkSurface;
                        btn.ForeColor = DarkFg;
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.FlatAppearance.BorderColor = DarkBorder;
                        break;

                    case Panel p:
                        p.BackColor = DarkBg;
                        break;

                    case Label lbl:
                        lbl.ForeColor = DarkFg;
                        break;

                    case ProgressBar:
                        // ProgressBar ignores BackColor on Windows; leave it
                        break;

                    case RadioButton rb:
                        rb.BackColor = DarkBg;
                        rb.ForeColor = DarkFg;
                        break;
                }
            }
            else
            {
                // Light — restore system defaults
                switch (c)
                {
                    case MenuStrip ms:
                        ms.BackColor = SystemColors.MenuBar;
                        ms.ForeColor = SystemColors.MenuText;
                        StyleMenuItems(ms.Items);
                        break;

                    case ListView lv:
                        lv.BackColor = SystemColors.Window;
                        lv.ForeColor = SystemColors.WindowText;
                        break;

                    case TextBox tb:
                        tb.BackColor = SystemColors.Window;
                        tb.ForeColor = SystemColors.WindowText;
                        break;

                    case RichTextBox:
                        break;

                    case Button btn when btn.BackColor == Color.FromArgb(0, 120, 212):
                        break;

                    case Button btn:
                        btn.BackColor = SystemColors.Control;
                        btn.ForeColor = SystemColors.ControlText;
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.FlatAppearance.BorderColor = SystemColors.ControlDark;
                        break;

                    case Panel p:
                        p.BackColor = SystemColors.Control;
                        break;

                    case Label lbl:
                        lbl.ForeColor = SystemColors.ControlText;
                        break;

                    case RadioButton rb:
                        rb.BackColor = SystemColors.Control;
                        rb.ForeColor = SystemColors.ControlText;
                        break;
                }
            }
        }

        private static void StyleMenuItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                item.BackColor = IsDark ? DarkSurface : SystemColors.MenuBar;
                item.ForeColor = IsDark ? DarkFg      : SystemColors.MenuText;

                if (item is ToolStripMenuItem tsmi && tsmi.HasDropDownItems)
                    StyleMenuItems(tsmi.DropDownItems);
            }
        }
    }
}

// Overlay.cs — WinForms topmost overlay window

using System;
using System.Drawing;
using System.Windows.Forms;

partial class HandTeller
{
    class Overlay : Form
    {
        Label _lblStatus;
        Label _lblHandName;
        Label _lblHole;
        Label _lblCommunity;
        Label _btnRetry;

        int _dragX, _dragY;

        // Scanner calls this to trigger an anchor re-scan
        public event Action OnRetry;

        public Overlay() {
            // ── Form setup ────────────────────────────────────────────────────
            Text            = "Hand Teller";
            FormBorderStyle = FormBorderStyle.None;
            TopMost         = true;
            StartPosition   = FormStartPosition.Manual;
            Location        = new Point(20, 20);
            Size            = new Size(320, 150);
            BackColor       = Color.FromArgb(26, 26, 46);
            Opacity         = 0.90;
            ShowInTaskbar   = false;

            // ── Drag support ──────────────────────────────────────────────────
            MouseDown += (s, e) => { _dragX = e.X; _dragY = e.Y; };
            MouseMove += (s, e) => {
                if (e.Button == MouseButtons.Left)
                    Location = new Point(Left + e.X - _dragX, Top + e.Y - _dragY);
            };

            // ── Labels ────────────────────────────────────────────────────────
            _lblStatus = MakeLabel("Initializing...", "Consolas", 9f, Color.FromArgb(136, 136, 136));
            _lblStatus.Location = new Point(10, 8);
            _lblStatus.Size = new Size(255, 16);

            _lblHandName = MakeLabel("", "Consolas", 22f, Color.FromArgb(255, 215, 0), bold: true);
            _lblHandName.Location = new Point(10, 28);
            _lblHandName.Size = new Size(295, 36);

            _lblHole = MakeLabel("", "Consolas", 12f, Color.FromArgb(224, 224, 224));
            _lblHole.Location = new Point(10, 68);
            _lblHole.Size = new Size(295, 20);

            _lblCommunity = MakeLabel("", "Consolas", 11f, Color.FromArgb(170, 170, 170));
            _lblCommunity.Location = new Point(10, 90);
            _lblCommunity.Size = new Size(295, 20);

            // Retry button — resets anchor, triggers re-scan
            _btnRetry = MakeLabel("↺ Retry", "Consolas", 9f, Color.FromArgb(100, 180, 255));
            _btnRetry.Location = new Point(10, 118);
            _btnRetry.Size = new Size(60, 18);
            _btnRetry.Cursor = Cursors.Hand;
            _btnRetry.Click += (s, e) => {
                _btnRetry.Enabled = false;
                _lblStatus.Text = "Re-scanning anchor...";
                if (OnRetry != null) OnRetry();
            };

            // Hint text
            var hint = MakeLabel("Run during an active hand for best results", "Consolas", 8f, Color.FromArgb(80, 80, 100));
            hint.Location = new Point(75, 120);
            hint.Size = new Size(230, 16);

            // Close button
            var close = MakeLabel("✕", "Consolas", 10f, Color.FromArgb(255, 68, 68));
            close.Location = new Point(295, 4);
            close.Size = new Size(20, 16);
            close.Cursor = Cursors.Hand;
            close.Click += (s, e) => Application.Exit();

            Controls.Add(_lblStatus);
            Controls.Add(_lblHandName);
            Controls.Add(_lblHole);
            Controls.Add(_lblCommunity);
            Controls.Add(_btnRetry);
            Controls.Add(hint);
            Controls.Add(close);
        }

        static Label MakeLabel(string text, string font, float size, Color color, bool bold = false) {
            return new Label {
                Text      = text,
                Font      = new Font(font, size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoSize  = false,
            };
        }

        // Re-enable the retry button (called after re-scan completes)
        public void EnableRetry() {
            if (InvokeRequired) { BeginInvoke(new Action(EnableRetry)); return; }
            _btnRetry.Enabled = true;
        }

        // Thread-safe update — call from any thread.
        public void SetState(string status, string handName, string hole, string community) {
            if (InvokeRequired) {
                BeginInvoke(new Action(() => SetState(status, handName, hole, community)));
                return;
            }
            _lblStatus.Text    = status;
            _lblHandName.Text  = handName;
            _lblHole.Text      = string.IsNullOrEmpty(hole)      ? "" : "Hole:      " + hole;
            _lblCommunity.Text = string.IsNullOrEmpty(community) ? "" : "Community: " + community;
        }
    }
}

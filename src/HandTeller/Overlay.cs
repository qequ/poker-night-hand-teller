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
        Label _btnRetry;
        Label _btnRestart;
        Timer _focusTimer;
        IntPtr _gameWindowHandle;

        int _dragX, _dragY;

        // Scanner calls this to trigger an anchor re-scan
        public event Action OnRetry;

        public Overlay() {
            // ── Form setup ────────────────────────────────────────────────────
            Text            = "Hand Teller";
            FormBorderStyle = FormBorderStyle.None;
            TopMost         = true;
            StartPosition   = FormStartPosition.Manual;
            // Position at ~3/4 down the screen, left side
            int screenH = Screen.PrimaryScreen.Bounds.Height;
            Location        = new Point(20, screenH * 3 / 4);
            Size            = new Size(280, 90);
            BackColor       = Color.FromArgb(10, 10, 20);
            Opacity         = 0.55;
            ShowInTaskbar   = false;

            // ── Drag support ──────────────────────────────────────────────────
            MouseDown += (s, e) => { _dragX = e.X; _dragY = e.Y; };
            MouseMove += (s, e) => {
                if (e.Button == MouseButtons.Left)
                    Location = new Point(Left + e.X - _dragX, Top + e.Y - _dragY);
            };

            // ── Labels ────────────────────────────────────────────────────────
            _lblStatus = MakeLabel("Initializing...", "Century Gothic", 8f, Color.FromArgb(120, 120, 120));
            _lblStatus.Location = new Point(10, 4);
            _lblStatus.Size = new Size(220, 14);

            _lblHandName = MakeLabel("", "Century Gothic", 22f, Color.FromArgb(255, 215, 0), bold: true);
            _lblHandName.Location = new Point(10, 20);
            _lblHandName.Size = new Size(260, 36);

            // Retry button (re-scan anchor)
            _btnRetry = MakeLabel("↺ Retry", "Century Gothic", 8f, Color.FromArgb(100, 180, 255));
            _btnRetry.Location = new Point(10, 60);
            _btnRetry.Size = new Size(50, 14);
            _btnRetry.Cursor = Cursors.Hand;
            _btnRetry.Click += (s, e) => {
                _btnRetry.Enabled = false;
                _lblStatus.Text = "Re-scanning...";
                if (OnRetry != null) OnRetry();
            };

            // Restart button (full re-launch)
            _btnRestart = MakeLabel("⟳ Restart", "Century Gothic", 8f, Color.FromArgb(100, 180, 255));
            _btnRestart.Location = new Point(70, 60);
            _btnRestart.Size = new Size(65, 14);
            _btnRestart.Cursor = Cursors.Hand;
            _btnRestart.Click += (s, e) => {
                Application.Restart();
            };

            // Close button
            var close = MakeLabel("✕", "Century Gothic", 9f, Color.FromArgb(255, 68, 68));
            close.Location = new Point(260, 4);
            close.Size = new Size(16, 14);
            close.Cursor = Cursors.Hand;
            close.Click += (s, e) => Application.Exit();

            Controls.Add(_lblStatus);
            Controls.Add(_lblHandName);
            Controls.Add(_btnRetry);
            Controls.Add(_btnRestart);
            Controls.Add(close);

            // Poll foreground window to show overlay only when game is active
            _focusTimer = new Timer();
            _focusTimer.Interval = 500;
            _focusTimer.Tick += (s, e) => {
                if (_gameWindowHandle == IntPtr.Zero) return;
                IntPtr fg = GetForegroundWindow();
                bool gameActive = (fg == _gameWindowHandle) || (fg == this.Handle);
                Visible = gameActive;
            };
            _focusTimer.Start();
        }

        // Set the game's main window handle so the overlay tracks its focus
        public void SetGameWindow(IntPtr hwnd) {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetGameWindow(hwnd))); return; }
            _gameWindowHandle = hwnd;
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
        public void SetState(string status, string handName) {
            if (InvokeRequired) {
                BeginInvoke(new Action(() => SetState(status, handName)));
                return;
            }
            _lblStatus.Text    = status;
            _lblHandName.Text  = handName;
        }
    }
}

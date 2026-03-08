// Program.cs — Entry point for HandTeller
// Single executable: scans Poker Night at the Inventory memory and shows hand overlay.
//
// Best practice: launch HandTeller AFTER the game has loaded into an active hand.
// The anchor scan finds the HumanPlayer object in the Lua heap. During the loading
// screen or menu the object may not yet be fully constructed, causing wrong reads.
// Use the "↺ Retry" button on the overlay to re-scan the anchor at any time.

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

partial class HandTeller
{
    const string GAME_PROCESS       = "CelebrityPoker";
    const int    POLL_MS            = 3000;
    // After this many consecutive failed reads, auto-reset the anchor.
    // 10 × 3s = 30s of no cards → likely stale anchor from loading/menu.
    const int    AUTO_RETRY_AFTER   = 10;

    static volatile bool _retryRequested = false;

    [STAThread]
    static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var overlay = new Overlay();

        // Wire up the retry button
        overlay.OnRetry += () => {
            _retryRequested = true;
        };

        overlay.Show();

        var thread = new Thread(() => ScannerLoop(overlay)) {
            IsBackground = true,
            Name = "Scanner"
        };
        thread.Start();

        Application.Run(overlay);
    }

    static int _gameProcessId;

    static void ScannerLoop(Overlay overlay) {
        // ── Find game process ─────────────────────────────────────────────────
        overlay.SetState("Looking for CelebrityPoker.exe...", "");
        IntPtr proc = IntPtr.Zero;
        while (proc == IntPtr.Zero) {
            Process game = null;
            foreach (var p in Process.GetProcessesByName(GAME_PROCESS))
                if (game == null || p.Id > game.Id) game = p;
            if (game != null) {
                _gameProcessId = game.Id;
                proc = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFO, false, game.Id);
                if (proc != IntPtr.Zero)
                    overlay.SetGameWindow(game.MainWindowHandle);
            }
            if (proc == IntPtr.Zero) {
                overlay.SetState("Game not found. Launch CelebrityPoker.exe.", "");
                Thread.Sleep(5000);
            }
        }

        // ── One-time TString scan (~15-20s) ───────────────────────────────────
        overlay.SetState("Scanning memory (~20s)...", "");
        while (!EnsureTStrings(proc)) {
            overlay.SetState("TString scan failed. Retrying...", "");
            Thread.Sleep(5000);
        }

        // ── Anchor scan + poll loop ───────────────────────────────────────────
        RunPollLoop(proc, overlay);

        CloseHandle(proc);
    }

    static bool IsGameRunning() {
        try {
            Process.GetProcessById(_gameProcessId);
            return true;
        } catch {
            return false;
        }
    }

    static void RunPollLoop(IntPtr proc, Overlay overlay) {
        FindAnchor(proc, overlay);

        string lastHand = null;
        int failStreak = 0;

        while (true) {
            // Exit if game closed
            if (!IsGameRunning()) {
                overlay.BeginInvoke(new Action(() => Application.Exit()));
                return;
            }

            // Manual retry requested via button
            if (_retryRequested) {
                _retryRequested = false;
                _anchorAddr = -1;
                failStreak = 0;
                lastHand = null;
                FindAnchor(proc, overlay);
                overlay.EnableRetry();
                continue;
            }

            try {
                var state = ReadCurrentCards(proc);

                if (state != null) {
                    failStreak = 0;
                    var hole      = state.Item1;
                    var community = state.Item2;

                    string handName = HandEvaluator.Evaluate(hole, community);
                    if (handName != lastHand) {
                        overlay.SetState("", handName);
                        lastHand = handName;
                    }
                } else {
                    failStreak++;

                    if (lastHand != null) {
                        overlay.SetState("Waiting for next hand...", "");
                        lastHand = null;
                    }

                    // Auto-retry: anchor is likely stale (loading screen / menu)
                    if (failStreak >= AUTO_RETRY_AFTER) {
                        failStreak = 0;
                        _anchorAddr = -1;
                        overlay.SetState("Re-scanning...", "");
                        FindAnchor(proc, overlay);
                    }
                }
            } catch {
                overlay.BeginInvoke(new Action(() => Application.Exit()));
                return;
            }

            Thread.Sleep(POLL_MS);
        }
    }

    static void FindAnchor(IntPtr proc, Overlay overlay) {
        overlay.SetState("Finding player anchor...", "");
        while (!EnsureAnchor(proc)) {
            overlay.SetState("Anchor not found, use Retry.", "");
            Thread.Sleep(5000);
            if (_retryRequested) return;
        }
        overlay.SetState("Ready", "");
    }
}

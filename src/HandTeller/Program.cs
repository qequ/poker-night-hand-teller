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

    static void ScannerLoop(Overlay overlay) {
        // ── Find game process ─────────────────────────────────────────────────
        overlay.SetState("Looking for CelebrityPoker.exe...", "", "", "");
        IntPtr proc = IntPtr.Zero;
        while (proc == IntPtr.Zero) {
            Process game = null;
            foreach (var p in Process.GetProcessesByName(GAME_PROCESS))
                if (game == null || p.Id > game.Id) game = p;
            if (game != null)
                proc = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFO, false, game.Id);
            if (proc == IntPtr.Zero) {
                overlay.SetState("Game not found. Launch CelebrityPoker.exe.", "", "", "");
                Thread.Sleep(5000);
            }
        }

        // ── One-time TString scan (~15-20s) ───────────────────────────────────
        overlay.SetState("Scanning memory (one-time, ~20s)...", "", "", "");
        while (!EnsureTStrings(proc)) {
            overlay.SetState("TString scan failed. Retrying...", "", "", "");
            Thread.Sleep(5000);
        }

        // ── Anchor scan + poll loop ───────────────────────────────────────────
        RunPollLoop(proc, overlay);

        CloseHandle(proc);
    }

    static void RunPollLoop(IntPtr proc, Overlay overlay) {
        FindAnchor(proc, overlay);

        string lastHole = null, lastComm = null;
        int failStreak = 0;

        while (true) {
            // Manual retry requested via button
            if (_retryRequested) {
                _retryRequested = false;
                _anchorAddr = -1;
                failStreak = 0;
                lastHole = lastComm = null;
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
                    string holeStr = string.Join(" ", hole.ToArray());
                    string commStr = string.Join(" ", community.ToArray());

                    if (holeStr != lastHole || commStr != lastComm) {
                        string handName = HandEvaluator.Evaluate(hole, community);
                        overlay.SetState("", handName, holeStr, commStr);
                        lastHole = holeStr;
                        lastComm = commStr;
                    }
                } else {
                    failStreak++;

                    if (lastHole != null) {
                        overlay.SetState("Waiting for next hand...", "", "", "");
                        lastHole = lastComm = null;
                    }

                    // Auto-retry: anchor is likely stale (loading screen / menu)
                    if (failStreak >= AUTO_RETRY_AFTER) {
                        failStreak = 0;
                        _anchorAddr = -1;
                        overlay.SetState("Re-scanning anchor (auto)...", "", "", "");
                        FindAnchor(proc, overlay);
                    }
                }
            } catch {
                overlay.SetState("Connection lost. Restart HandTeller.", "", "", "");
                return;
            }

            Thread.Sleep(POLL_MS);
        }
    }

    static void FindAnchor(IntPtr proc, Overlay overlay) {
        overlay.SetState("Finding player anchor...", "", "", "");
        while (!EnsureAnchor(proc)) {
            overlay.SetState("Anchor not found — launch during an active hand, or use ↺ Retry.", "", "", "");
            Thread.Sleep(5000);
            if (_retryRequested) return; // let the poll loop handle it
        }
        overlay.SetState("Ready", "", "", "");
    }
}

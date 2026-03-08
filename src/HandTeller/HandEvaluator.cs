// HandEvaluator.cs — Pure C# poker hand evaluator (no external dependencies)
// Evaluates best 5-card hand from any number of cards (2-7).

using System;
using System.Collections.Generic;

static class HandEvaluator
{
    // ── Card parsing ──────────────────────────────────────────────────────────
    // Card strings from AnchorNav: "2s","10h","Ah","Kd", etc.
    // Returns [rank2-14, suit1-4] or null.
    public static int[] ParseCard(string s) {
        if (string.IsNullOrEmpty(s) || s.Length < 2) return null;
        char suitChar = s[s.Length - 1];
        string rankStr = s.Substring(0, s.Length - 1);
        int suit;
        switch (suitChar) {
            case 's': suit = 1; break;
            case 'h': suit = 2; break;
            case 'd': suit = 3; break;
            case 'c': suit = 4; break;
            default: return null;
        }
        int rank;
        switch (rankStr) {
            case "2":  rank =  2; break; case "3":  rank =  3; break;
            case "4":  rank =  4; break; case "5":  rank =  5; break;
            case "6":  rank =  6; break; case "7":  rank =  7; break;
            case "8":  rank =  8; break; case "9":  rank =  9; break;
            case "10": rank = 10; break; case "J":  rank = 11; break;
            case "Q":  rank = 12; break; case "K":  rank = 13; break;
            case "A":  rank = 14; break;
            default: return null;
        }
        return new int[] { rank, suit };
    }

    // ── Public entry point ────────────────────────────────────────────────────
    // cardStrs: mix of hole + community card strings.
    // Returns hand name, or pre-flop label if < 5 cards total.
    public static string Evaluate(List<string> hole, List<string> community) {
        if (hole.Count < 2) return "";

        // Pre-flop: no community cards yet
        if (community.Count == 0) {
            int[] c1 = ParseCard(hole[0]);
            int[] c2 = ParseCard(hole[1]);
            if (c1 != null && c2 != null && c1[0] == c2[0]) return "Pocket Pair";
            return "Pre-flop";
        }

        // Build full card list
        var all = new List<int[]>();
        foreach (var s in hole)      { var c = ParseCard(s); if (c != null) all.Add(c); }
        foreach (var s in community) { var c = ParseCard(s); if (c != null) all.Add(c); }

        if (all.Count < 5) return "Waiting...";

        // Try every C(n,5) combination, keep best score
        int bestScore = -1;
        var combo = new int[5][];
        Combinations(all, 0, combo, 0, ref bestScore);
        return ScoreToName(bestScore);
    }

    // ── C(n,5) combination enumeration ───────────────────────────────────────
    static void Combinations(List<int[]> cards, int start, int[][] combo, int depth, ref int bestScore) {
        if (depth == 5) {
            int s = Score5(combo);
            if (s > bestScore) bestScore = s;
            return;
        }
        int remaining = 5 - depth;
        for (int i = start; i <= cards.Count - remaining; i++) {
            combo[depth] = cards[i];
            Combinations(cards, i + 1, combo, depth + 1, ref bestScore);
        }
    }

    // ── Score a 5-card hand ───────────────────────────────────────────────────
    // Returns an integer: higher = better hand.
    // Top byte = hand class (0=high card ... 9=royal flush).
    static int Score5(int[][] c) {
        // Extract ranks and suits
        int[] r = new int[5], s = new int[5];
        for (int i = 0; i < 5; i++) { r[i] = c[i][0]; s[i] = c[i][1]; }

        bool flush = s[0] == s[1] && s[1] == s[2] && s[2] == s[3] && s[3] == s[4];

        Array.Sort(r);
        bool straight = (r[4] - r[0] == 4 && r[1] == r[0]+1 && r[2] == r[0]+2 && r[3] == r[0]+3)
                     || (r[4] == 14 && r[0] == 2 && r[1] == 3 && r[2] == 4 && r[3] == 5); // A-2-3-4-5 wheel

        // Count rank frequencies
        var freq = new Dictionary<int, int>();
        foreach (int rv in r) freq[rv] = freq.ContainsKey(rv) ? freq[rv] + 1 : 1;
        var counts = new List<int>(freq.Values);
        counts.Sort((a, b) => b - a); // descending

        int handClass;
        if (flush && straight)          handClass = (r[4] == 14 && r[3] == 13) ? 9 : 8; // royal / straight flush
        else if (counts[0] == 4)        handClass = 7; // four of a kind
        else if (counts[0] == 3 && counts.Count >= 2 && counts[1] == 2) handClass = 6; // full house
        else if (flush)                 handClass = 5;
        else if (straight)              handClass = 4;
        else if (counts[0] == 3)        handClass = 3; // three of a kind
        else if (counts[0] == 2 && counts.Count >= 2 && counts[1] == 2) handClass = 2; // two pair
        else if (counts[0] == 2)        handClass = 1; // one pair
        else                            handClass = 0; // high card

        // Tiebreaker: pack top kicker into lower bits (sort descending)
        Array.Sort(r, (a, b) => b - a);
        int kicker = r[0] * 15*15*15*15 + r[1] * 15*15*15 + r[2] * 15*15 + r[3] * 15 + r[4];
        return (handClass << 20) | kicker;
    }

    static readonly string[] HAND_NAMES = {
        "High Card", "One Pair", "Two Pair", "Three of a Kind",
        "Straight", "Flush", "Full House", "Four of a Kind",
        "Straight Flush", "Royal Flush"
    };

    static string ScoreToName(int score) {
        if (score < 0) return "";
        int cls = score >> 20;
        if (cls < 0 || cls >= HAND_NAMES.Length) return "";
        return HAND_NAMES[cls];
    }
}

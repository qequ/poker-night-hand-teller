// AnchorNav.cs — Lua heap navigation: HumanPlayer -> cHand -> hole/community cards
// Navigation chain:
//   playerType='human' node (anchor)
//     -> cHand -> Hand table
//       -> holeCards -> CardContainer -> cardList -> Card[]  (2 hole cards)
//       -> cards     -> CardContainer -> cardList -> Card[]  (all cards; [2+] = community)

using System;
using System.Collections.Generic;

partial class HandTeller
{
    // ── Session cache: TString pointers are stable for the Lua VM lifetime ───
    static Dictionary<string, long> _tsCache  = null;
    static long                     _anchorAddr = -1;

    // ── GetHashValueAndTT: look up a TString-keyed value in a table's hash ───
    static long GetHashValueAndTT(IntPtr proc, long tableAddr, long keyTStrPtr, out int valTT) {
        valTT = -1;
        var tbl = ReadTable(proc, tableAddr); if (tbl == null) return -1;
        int lsn = tbl[TBL_LSIZENODE]; if (lsn < 0 || lsn > 20) return -1;
        long nb = BitConverter.ToInt64(tbl, TBL_NODE); if (!ValidPtr(nb)) return -1;
        int nc = 1 << lsn;
        var nodes = Read(proc, nb, nc * NODE_SIZE); if (nodes == null) return -1;
        for (int k = 0; k < nc; k++) {
            int o = k * NODE_SIZE;
            if (o + NODE_KEY_TT + 4 > nodes.Length) break;
            long kPtr = BitConverter.ToInt64(nodes, o + NODE_KEY);
            int  kTT  = BitConverter.ToInt32(nodes, o + NODE_KEY_TT);
            if ((kTT & 0x3F) == LUA_TSTRING && kPtr == keyTStrPtr) {
                valTT = BitConverter.ToInt32(nodes, o + NODE_VAL_TT);
                return BitConverter.ToInt64(nodes, o + NODE_VAL);
            }
        }
        return -1;
    }

    // ── GetArrayCard: read rank+suit from a Card table's Lua array part ──────
    // Card.lua stores: self[1]=rank(2-14 float), self[2]=suit(1-4 float)
    static int[] GetArrayCard(IntPtr proc, long cardTableAddr) {
        var tbl = ReadTable(proc, cardTableAddr); if (tbl == null) return null;
        int sa = BitConverter.ToInt32(tbl, TBL_SIZEARRAY);
        long ap = BitConverter.ToInt64(tbl, TBL_ARRAY);
        if (sa < 2 || !ValidPtr(ap)) return null;
        var arr = Read(proc, ap, 2 * TV_SIZE);
        if (arr == null || arr.Length < 2 * TV_SIZE) return null;
        if (BitConverter.ToInt32(arr, TV_TT)           != LUA_TNUMBER) return null;
        if (BitConverter.ToInt32(arr, TV_SIZE + TV_TT) != LUA_TNUMBER) return null;
        float r = BitConverter.ToSingle(arr, TV_VALUE);
        float s = BitConverter.ToSingle(arr, TV_SIZE + TV_VALUE);
        if (r < 2f || r > 14f || r != (float)(int)r) return null;
        if (s < 1f || s > 4f  || s != (float)(int)s) return null;
        return new int[] { (int)r, (int)s };
    }

    // ── ReadCardList: CardContainer table -> cardList -> list of card strings ─
    static List<string> ReadCardList(IntPtr proc, long containerPtr, long cardListTStr) {
        var result = new List<string>();
        int clTT;
        long cardListPtr = GetHashValueAndTT(proc, containerPtr, cardListTStr, out clTT);
        if (!ValidPtr(cardListPtr)) return result;
        var listTbl = ReadTable(proc, cardListPtr); if (listTbl == null) return result;
        int sa = BitConverter.ToInt32(listTbl, TBL_SIZEARRAY);
        long ap = BitConverter.ToInt64(listTbl, TBL_ARRAY);
        if (sa < 1 || !ValidPtr(ap)) return result;
        var arrBuf = Read(proc, ap, Math.Min(sa, 7) * TV_SIZE); if (arrBuf == null) return result;
        for (int k = 0; k < Math.Min(sa, 7); k++) {
            int o = k * TV_SIZE;
            if (o + TV_SIZE > arrBuf.Length) break;
            long cardPtr = BitConverter.ToInt64(arrBuf, o + TV_VALUE);
            if (!ValidPtr(cardPtr)) continue;
            int[] cd = GetArrayCard(proc, cardPtr);
            if (cd != null) result.Add(CardStr(cd[0], cd[1]));
        }
        return result;
    }

    // ── EnsureTStrings: one-time scan, cached for the session ────────────────
    static bool EnsureTStrings(IntPtr proc) {
        if (_tsCache != null) return true;
        string[] needed = { "playerType", "human", "cHand", "holeCards", "cards", "cardList", "name", "Player" };
        var map = new Dictionary<string, long>();
        foreach (var n in needed) map[n] = FindTString(proc, n);
        if (map["cHand"] < 0 || map["holeCards"] < 0 || map["cardList"] < 0) return false;
        _tsCache = map;
        return true;
    }

    // ── EnsureAnchor: find + cache HumanPlayer hash node address ─────────────
    static bool EnsureAnchor(IntPtr proc) {
        if (_anchorAddr >= 0) {
            // Validate cached node still matches playerType='human'
            var nd = Read(proc, _anchorAddr, NODE_SIZE);
            if (nd != null) {
                long kPtr = BitConverter.ToInt64(nd, NODE_KEY);
                long vPtr = BitConverter.ToInt64(nd, NODE_VAL);
                int  kTT  = BitConverter.ToInt32(nd, NODE_KEY_TT);
                if ((kTT & 0x3F) == LUA_TSTRING && kPtr == _tsCache["playerType"] && vPtr == _tsCache["human"])
                    return true;
            }
            _anchorAddr = -1; // stale — re-scan
        }
        var ts = _tsCache;
        if (ts["playerType"] >= 0 && ts["human"] >= 0) {
            foreach (var nd in FindNodesWithKey(proc, ts["playerType"])) {
                if ((nd[2] & 0x3F) == LUA_TSTRING && nd[1] == ts["human"]) {
                    _anchorAddr = nd[0]; return true;
                }
            }
        }
        // Fallback: name='Player'
        if (ts["name"] >= 0 && ts["Player"] >= 0) {
            foreach (var nd in FindNodesWithKey(proc, ts["name"])) {
                if ((nd[2] & 0x3F) == LUA_TSTRING && nd[1] == ts["Player"]) {
                    _anchorAddr = nd[0]; return true;
                }
            }
        }
        return false;
    }

    // ── FindCHand: locate the current Hand table via the anchor ──────────────
    static long FindCHand(IntPtr proc) {
        // Fast path: cHand node is in the same hash table as anchor (±800 bytes)
        for (int delta = -800; delta <= 800; delta += NODE_SIZE) {
            if (delta == 0) continue;
            long cand = _anchorAddr + delta;
            var nd = Read(proc, cand, NODE_SIZE);
            if (nd == null || nd.Length < NODE_KEY_TT + 4) continue;
            long kPtr = BitConverter.ToInt64(nd, NODE_KEY);
            int  kTT  = BitConverter.ToInt32(nd, NODE_KEY_TT);
            if ((kTT & 0x3F) == LUA_TSTRING && kPtr == _tsCache["cHand"]) {
                int  vTT  = BitConverter.ToInt32(nd, NODE_VAL_TT);
                long vPtr = BitConverter.ToInt64(nd, NODE_VAL);
                if ((vTT & 0x3F) == LUA_TTABLE && ValidPtr(vPtr)) return vPtr;
            }
        }
        // Slow fallback: scan all cHand nodes, pick closest to anchor
        long best = -1, bestDist = long.MaxValue;
        foreach (var nd in FindNodesWithKey(proc, _tsCache["cHand"])) {
            if ((nd[2] & 0x3F) != LUA_TTABLE || !ValidPtr(nd[1])) continue;
            long dist = Math.Abs(nd[0] - _anchorAddr);
            if (dist < bestDist) { bestDist = dist; best = nd[1]; }
        }
        return best;
    }

    // ── ReadCurrentCards: navigate to current hand, return (hole, community) ─
    // Returns null if no active hand.
    static Tuple<List<string>, List<string>> ReadCurrentCards(IntPtr proc) {
        long cHandPtr = FindCHand(proc);
        if (cHandPtr < 0) return null;

        int hcTT;
        long holeCardsPtr = GetHashValueAndTT(proc, cHandPtr, _tsCache["holeCards"], out hcTT);
        if (!ValidPtr(holeCardsPtr)) return null;

        var hole = ReadCardList(proc, holeCardsPtr, _tsCache["cardList"]);
        if (hole.Count < 2) return null;

        // Hand.cards is sorted by rank after evaluation, so we can't skip by index.
        // Instead: community = all cards minus the hole cards (set subtraction).
        var community = new List<string>();
        int csTT;
        long allCardsPtr = GetHashValueAndTT(proc, cHandPtr, _tsCache["cards"], out csTT);
        if (ValidPtr(allCardsPtr)) {
            var all = ReadCardList(proc, allCardsPtr, _tsCache["cardList"]);
            var holeSet = new System.Collections.Generic.HashSet<string>(hole);
            foreach (var card in all)
                if (holeSet.Remove(card)) { /* consumed one hole card match */ }
                else community.Add(card);
        }

        return Tuple.Create(hole, community);
    }
}

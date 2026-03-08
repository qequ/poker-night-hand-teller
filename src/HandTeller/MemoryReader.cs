// MemoryReader.cs — Win32 + Lua 5.1 memory reading primitives
// Part of HandTeller — Poker Night at the Inventory hand overlay mod

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

partial class HandTeller
{
    // ── Win32 ─────────────────────────────────────────────────────────────────
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool   CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern bool   ReadProcessMemory(IntPtr proc, IntPtr addr, byte[] buf, int size, out int read);
    [DllImport("kernel32.dll")] static extern int    VirtualQueryEx(IntPtr proc, IntPtr addr, out MBI info, int size);

    [StructLayout(LayoutKind.Sequential)]
    struct MBI {
        public IntPtr BaseAddress, AllocationBase;
        public uint   AllocationProtect;
        public IntPtr RegionSize;
        public uint   State, Protect, Type;
    }

    const uint MEM_COMMIT             = 0x1000;
    const uint PAGE_GUARD             = 0x100;
    const uint PROCESS_VM_READ        = 0x0010;
    const uint PROCESS_QUERY_INFO     = 0x0400;

    // ── Lua 5.1 64-bit struct layout (Telltale build, verified from raw memory) ─
    // Table (64 bytes):
    //   [GCnext:8][tt:1=5][marked:1][flags:1][lsizenode:1][telltale?:4]
    //   [metatable*:8][array*:8][node*:8][lastfree*:8][gclist*:8][sizearray:4][pad:4]
    // Node (40 bytes):
    //   [i_val.val:8][i_val.tt:4][pad:4][i_key.val:8][i_key.tt:4][pad:4][i_key.next*:8]
    // TValue (16 bytes):
    //   [value:8][tt:4][pad:4]  (LUA_TNUMBER: value is float32)
    // TString: [GCnext:8][tt:1=4][marked:1][rsv:1][pad:1][hash:4][pad:4][len:4] + data

    const int TBL_LSIZENODE = 11;
    const int TBL_META      = 16;
    const int TBL_ARRAY     = 24;
    const int TBL_NODE      = 32;
    const int TBL_LASTFREE  = 40;
    const int TBL_SIZEARRAY = 56;
    const int TBL_SIZE      = 64;

    const int NODE_VAL    =  0;
    const int NODE_VAL_TT =  8;
    const int NODE_KEY    = 16;
    const int NODE_KEY_TT = 24;
    const int NODE_NEXT   = 32;
    const int NODE_SIZE   = 40;

    const int TV_VALUE = 0;
    const int TV_TT    = 8;
    const int TV_SIZE  = 16;

    const int LUA_TNUMBER = 3;
    const int LUA_TSTRING = 4;
    const int LUA_TTABLE  = 5;

    // ── Card encoding (Card.lua): rank 2-14 (Ace=14), suit S=1 H=2 D=3 C=4 ──
    static readonly string[] RANKS = { "2","3","4","5","6","7","8","9","10","J","Q","K","A" };
    static readonly string[] SUITS = { "s","h","d","c" };

    static string CardStr(int luaRank, int luaSuit) {
        // luaRank: 2-14, luaSuit: 1-4
        int r = luaRank - 2; // → 0-12
        int s = luaSuit - 1; // → 0-3
        if (r < 0 || r > 12 || s < 0 || s > 3) return "??";
        return RANKS[r] + SUITS[s];
    }

    // ── Memory helpers ────────────────────────────────────────────────────────
    static bool ValidPtr(long p) { return p > 0x10000 && p < 0x7FF000000000L; }

    static byte[] Read(IntPtr proc, long addr, int size) {
        if (!ValidPtr(addr)) return null;
        var buf = new byte[size]; int br;
        if (!ReadProcessMemory(proc, (IntPtr)addr, buf, size, out br) || br < size) return null;
        return buf;
    }

    // Read TString content (verifies tt=4). Returns null on mismatch.
    static string ReadTString(IntPtr proc, long ptr) {
        var hdr = Read(proc, ptr, 28); if (hdr == null) return null;
        if (hdr[8] != LUA_TSTRING) return null;
        int len = BitConverter.ToInt32(hdr, 16);
        if (len <= 0 || len > 256) return null;
        var data = Read(proc, ptr + 24, len); if (data == null) return null;
        return Encoding.ASCII.GetString(data);
    }

    // Read Table header (64 bytes), verifies tt=LUA_TTABLE. Returns null on failure.
    static byte[] ReadTable(IntPtr proc, long ptr) {
        if (!ValidPtr(ptr)) return null;
        var buf = Read(proc, ptr, TBL_SIZE); if (buf == null) return null;
        if ((buf[8] & 0x3F) != LUA_TTABLE) return null;
        return buf;
    }

    // Scan all committed VM pages for a TString with the given content.
    // TStrings are interned per Lua state — there is exactly one per unique string.
    static long FindTString(IntPtr proc, string target) {
        byte[] needle = Encoding.ASCII.GetBytes(target);
        int tlen = needle.Length;
        int mbiSz = Marshal.SizeOf(typeof(MBI));
        for (long addr = 0; addr < 0x7FF000000000L; ) {
            MBI m; if (VirtualQueryEx(proc, (IntPtr)addr, out m, mbiSz) == 0) break;
            long rsz = m.RegionSize.ToInt64(), ba = m.BaseAddress.ToInt64();
            if (m.State == MEM_COMMIT && (m.Protect & 0xEE) != 0 && (m.Protect & PAGE_GUARD) == 0) {
                int csz = (int)Math.Min(rsz, 32L * 1024 * 1024);
                var buf = new byte[csz]; int br;
                if (ReadProcessMemory(proc, (IntPtr)ba, buf, csz, out br) && br > tlen + 24) {
                    for (int i = 0; i <= br - tlen - 24; i++) {
                        if (buf[i + 8] != LUA_TSTRING) continue;
                        if (BitConverter.ToInt32(buf, i + 16) != tlen) continue;
                        if (i + 24 + tlen > br) continue;
                        bool ok = true;
                        for (int j = 0; j < tlen; j++) if (buf[i + 24 + j] != needle[j]) { ok = false; break; }
                        if (ok) return ba + i;
                    }
                }
            }
            addr = ba + rsz; if (addr <= 0) break;
        }
        return -1;
    }

    // Find all hash Nodes where i_key matches the given TString pointer.
    // Tries both key_tt=0x44 (with BIT_ISCOLLECTABLE) and 0x04.
    // Returns list of [nodeAddr, valPtr, valTT, keyTT].
    static List<long[]> FindNodesWithKey(IntPtr proc, long tsPtr) {
        var res = new List<long[]>();
        var pats = new byte[][] { new byte[12], new byte[12] };
        Array.Copy(BitConverter.GetBytes(tsPtr), 0, pats[0], 0, 8);
        Array.Copy(BitConverter.GetBytes(0x44),  0, pats[0], 8, 4);
        Array.Copy(BitConverter.GetBytes(tsPtr), 0, pats[1], 0, 8);
        Array.Copy(BitConverter.GetBytes(0x04),  0, pats[1], 8, 4);
        int mbiSz = Marshal.SizeOf(typeof(MBI));
        for (long addr = 0; addr < 0x7FF000000000L; ) {
            MBI m; if (VirtualQueryEx(proc, (IntPtr)addr, out m, mbiSz) == 0) break;
            long rsz = m.RegionSize.ToInt64(), ba = m.BaseAddress.ToInt64();
            if (m.State == MEM_COMMIT && (m.Protect & 0xEE) != 0 && (m.Protect & PAGE_GUARD) == 0) {
                int csz = (int)Math.Min(rsz, 32L * 1024 * 1024);
                var buf = new byte[csz]; int br;
                if (ReadProcessMemory(proc, (IntPtr)ba, buf, csz, out br) && br >= 28) {
                    foreach (var pat in pats) {
                        byte p0 = pat[0];
                        for (int i = NODE_KEY; i <= br - 12; i++) {
                            if (buf[i] != p0) continue;
                            bool ok = true;
                            for (int j = 1; j < 12; j++) if (buf[i + j] != pat[j]) { ok = false; break; }
                            if (!ok) continue;
                            long nodeAddr = ba + i - NODE_KEY;
                            long vPtr = BitConverter.ToInt64(buf, i - NODE_KEY + NODE_VAL);
                            int  vTT  = BitConverter.ToInt32(buf, i - NODE_KEY + NODE_VAL_TT);
                            int  kTT  = BitConverter.ToInt32(buf, i + NODE_KEY_TT - NODE_KEY);
                            res.Add(new long[] { nodeAddr, vPtr, vTT, kTT });
                        }
                    }
                }
            }
            addr = ba + rsz; if (addr <= 0) break;
        }
        return res;
    }
}

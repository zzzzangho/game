using System.Collections.Generic;

namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 원작 다음 관심사 선택 (ROM 0x08028524 와 0x08119004·0x081190D0·0x0811925C·0x081191A4·0x081192F8·0x081193F4 의 손 이식).
    /// 결과는 바로 인물 레코드에 쓰지 않고 대기표(0x0203BD20 번호×8, 0x0203BD40 관심사×8)에 넣으며 (0x08028938),
    /// ApplyPending (0x080289C4) 이 가족 레코드 +0x80 에 옮긴다. 선택 직후 게이지 = 관심사 +0x12, 날짜 = 1, 화살 표시 해제.
    /// </summary>
    public sealed class OrigSelect
    {
        public const uint PendIds = 0x0203BD20, PendKeys = 0x0203BD40, Entries = 0x085BD4A0;
        readonly OrigMem m; readonly OrigVm vm; readonly OrigRules rules;
        /// <summary>원작에서 초기화하지 않은 지역 변수(b15)를 쓰는 경로가 있다 — 그 값 (원작에서는 직전 호출들이 스택에 남긴 값).</summary>
        public int UninitB15;
        public string LastPath = "";

        public OrigSelect(OrigMem m, OrigVm vm, OrigRules rules) { this.m = m; this.vm = vm; this.rules = rules; }

        uint Rand() { return m.Rand(); }

        uint EntryAddr(uint t, uint i) { return m.R32(m.R32(Entries + 4 * t) + 4 * i); }

        bool Pred(uint entry) { return vm.Call(m.R32(entry + 8).ToString("X8"), entry) != 0; }

        static uint Pack(uint t, uint i) { return (t & 0xFFFF) | (i << 16); }

        /// <summary>F0/F2/F3 공통: 후보 목록에서 무작위로 뽑아 성별·(유형)·판정 함수를 통과할 때까지.</summary>
        uint PickList(uint cur, uint table, List<int> list, uint gender, int needType)
        {
            if (list == null || list.Count == 0) throw new OrigUnmodeled("후보 없음 (원작은 여기서 멈춘다) 표 " + table);
            bool first = true;
            for (int guard = 0; guard < 100000; guard++)
            {
                uint idx = (uint)list[(int)(Rand() % (uint)list.Count)];
                if (first && Pack(table, idx) == cur) { first = false; continue; }
                uint e = EntryAddr(table, idx);
                if (needType >= 0 && m.R8(e + 0x0F) != (uint)needType) continue;
                uint g = m.R8(e + 0x10);
                if (g != 2 && g != gender) continue;
                if (Pred(e)) return idx;
            }
            throw new OrigUnmodeled("관심사 후보를 고르지 못함");
        }

        /// <summary>0x081191A4: 직업 후보 번호 하나.</summary>
        uint JobCandidate(uint job, uint rank, uint b15)
        {
            if (job >= rules.Jobs.Count) throw new OrigUnmodeled("직업 표 밖 " + job);
            var map = rules.Jobs[(int)job].Map;
            int j2 = b15 < map.Count ? map[(int)b15] : throw new OrigUnmodeled("직업 b15 " + b15);
            var J = rules.Jobs[j2];
            var rl = rank < J.Ranks.Count ? J.Ranks[(int)rank] : throw new OrigUnmodeled("직급 표 밖 " + rank);
            uint n1 = (uint)J.List.Count, r = Rand() % (n1 + (uint)rl.Count);
            return r < n1 ? (uint)J.List[(int)r] : (uint)rl[(int)(r - n1)];
        }

        uint PickJob(uint cur, uint job, uint rank, uint b15)    // 0x0811925C
        {
            bool first = true;
            for (int guard = 0; guard < 100000; guard++)
            {
                uint idx = JobCandidate(job, rank, b15);
                if (first && Pack(1, idx) == cur) { first = false; continue; }
                if (Pred(EntryAddr(1, idx))) return idx;
            }
            throw new OrigUnmodeled("직업 관심사를 고르지 못함");
        }

        /// <summary>0x081192F8: 특별 후보표. 찾으면 true 와 (표, 번호).</summary>
        bool PickSpecial(uint cur, uint set, uint gender, out uint t, out uint i)
        {
            t = i = 0xFFFF;
            if (set >= rules.Specials.Count) throw new OrigUnmodeled("특별 후보표 밖 " + set);
            bool first = true;
            foreach (var ent in rules.Specials[(int)set])
            {
                if ((Rand() & 0xFF) >= (uint)ent[0]) continue;
                uint tt = (uint)ent[1], ii = (uint)ent[2];
                t = tt; i = ii;
                if (first && Pack(tt, ii) == cur) { first = false; continue; }
                uint e = EntryAddr(tt, ii);
                if ((m.R8(e + 0x10) & gender) != 0 && Pred(e)) return true;
            }
            t = i = 0xFFFF;
            return false;
        }

        /// <summary>0x08028524(인물 레코드, 시대, 나이 단계).</summary>
        public void Select(uint p, uint era, uint stage)
        {
            uint cur = m.R32(p + 0x80), gender = m.R8(p + 0x31);
            int mode = 5; uint kt = 0, ki = 0; uint b15 = (uint)UninitB15;
            uint p61 = m.R8(p + 0x61), p60 = m.R8(p + 0x60);
            if ((p61 & 8) != 0)
            {
                kt = 3; mode = 3;
                ki = PickList(cur, 3, Cand(3, stage, era), gender, (int)(p61 & 3));
                m.W8(p + 0x61, 0);
            }
            else if (p60 == 2) { if (Rand() % 3 != 0) mode = 0; }
            else if (p60 == 4) { if (Rand() % 3 != 0) mode = 2; }
            else
            {
                bool found = false;
                uint set = m.R16(p + 0x3E);
                if (set != 0xFFFF) found = PickSpecial(cur, set, gender, out kt, out ki);
                if (found) mode = 4;
                else if (p60 == 3) { if (Rand() % 3 != 0) mode = 1; }
            }
            if (mode == 5)
            {
                b15 = (uint)rules.B15Table[(int)(Rand() % 3)];
                mode = rules.ModeTable[(int)stage, (int)(Rand() & 15)];
            }
            switch (mode)
            {
                case 0: kt = 0; ki = PickList(cur, 0, Cand(0, stage, era), gender, -1); break;
                case 1: kt = 1; ki = PickJob(cur, m.R8(p + 0x58), m.R8(p + 0x59), b15); break;
                case 2: kt = 2; ki = PickList(cur, 2, Cand(2, stage, era), gender, -1); break;
            }
            LastPath = "mode" + mode;
            Pend(kt, ki, m.R16(p + 0x3C));
            uint e = EntryAddr(kt, ki);
            m.W8(p + 0x5A, m.R8(e + 0x12));
            m.W16(p + 0x48, 1);
            m.W8(p + 0x69, m.R8(p + 0x69) & 0xF8);
        }

        List<int> Cand(int type, uint stage, uint era)
        {
            if (!rules.Candidates.TryGetValue(type, out var a) || stage >= 8 || era >= 4) return null;
            return a[stage, era];
        }

        /// <summary>0x08028938: 대기표에 넣기 (같은 번호 또는 빈 칸 중 앞쪽).</summary>
        void Pend(uint t, uint i, uint id)
        {
            for (uint j = 0; j < 8; j++)
            {
                uint cid = m.R16(PendIds + 2 * j);
                if (cid == id || cid == 0xFFFF)
                {
                    m.W32(PendKeys + 4 * j, Pack(t, i)); m.W16(PendIds + 2 * j, id);
                    return;
                }
            }
        }

        /// <summary>0x080289C4: 대기 중인 관심사를 가족 레코드 +0x80 에 옮긴다.</summary>
        public static void ApplyPending(OrigMem m)
        {
            for (uint j = 0; j < 8; j++)
            {
                uint id = m.R16(PendIds + 2 * j);
                int n = -1;
                for (int k = 0; k < 8; k++) if (m.R16(OrigMem.PersonAddr(k) + 0x3C) == id) { n = k; break; }
                if (n < 0) continue;
                if (m.R16(PendKeys + 4 * j) == 0xFFFF) continue;
                m.W32(OrigMem.PersonAddr(n) + 0x80, m.R32(PendKeys + 4 * j));
                m.W16(PendKeys + 4 * j, 0xFFFF);
                m.W16(PendIds + 2 * j, 0xFFFF);
            }
        }

        /// <summary>0x08111888: 나이 → 단계 (≤3:0 ≤6:1 ≤12:2 ≤15:3 ≤22:4 ≤34:5 ≤60:6 그 위:7).</summary>
        public static uint Stage(int age)
        {
            uint a = (uint)age;
            if (a <= 3) return 0; if (a <= 6) return 1; if (a <= 12) return 2; if (a <= 15) return 3;
            if (a <= 22) return 4; if (a <= 34) return 5; if (a <= 60) return 6; return 7;
        }
    }
}

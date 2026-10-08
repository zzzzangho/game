namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 원작 관계 슬롯표(EWRAM 0x0203C3E0, u16×28)와 관계 판정의 손 이식. 원작 판정 함수는 "슬롯 k 의 사람"으로 가족을 가리킨다.
    /// 슬롯: 0 본인 · 1/2 증조부/증조모 · 3/4 조부/조모 · 5/6 아버지/어머니 · 7~10 자녀 · 11~14 손주 · 15 배우자 ·
    ///       16 (같이 사는 첫 자녀의) 배우자 · 17 손주의 배우자 · 18/19 같이 사는 첫 자녀 부부(남·여 순) · 20~22 형제 · 27 가족 +0x66C
    /// 족보 레코드(0x0202EB9C + 60×번호): +0x10 아버지 · +0x12 어머니 · +0x14 배우자 · +0x16 자녀 4명 · +0x31 성별.
    /// 근거: ROM 0x08110B90 · 0x08110608 · 0x08110690 · 0x081107A8 · 0x081108C0 · 0x081109C0 · 0x08111384 · 0x08115778
    /// (원작 실행 결과와 비교: tools/romlift/verify_slots.py 287건, verify_rel.py 15,512건 일치)
    /// </summary>
    public static class OrigFamily
    {
        public const uint None = 0xFFFF;
        static uint G(uint id) { return OrigMem.GeneAddr(id); }

        /// <summary>0x08110608: 가족 명단(가족 +0x2538, 28바이트, 수 +0x6A3)에서 번호 위치(1부터), 없으면 0xFFFF.</summary>
        public static uint InHouse(OrigMem m, uint x)
        {
            if (x == None) return None;
            uint n = m.R8(OrigMem.Fam + 0x6A3);
            for (uint i = n; i >= 1; i--)
                if (m.R16(OrigMem.Fam + 0x2538 + 28 * (i - 1)) == x) return i;
            return None;
        }

        static uint AncM(OrigMem m, uint x, int n)          // 0x08110690
        {
            if (n == 1) return m.R16(G(x) + 0x10);
            uint f = m.R16(G(x) + 0x10), mo = m.R16(G(x) + 0x12);
            if (m.R16(G(f) + 0x10) != None) return AncM(m, f, n - 1);
            if (m.R16(G(mo) + 0x10) != None) return AncM(m, mo, n - 1);
            return None;
        }

        static uint AncF(OrigMem m, uint x, int n)          // 0x081107A8
        {
            if (n == 1) return m.R16(G(x) + 0x12);
            uint f = m.R16(G(x) + 0x10), mo = m.R16(G(x) + 0x12);
            if (m.R16(G(mo) + 0x12) != None) return AncF(m, mo, n - 1);
            if (m.R16(G(f) + 0x12) != None) return AncF(m, f, n - 1);
            return None;
        }

        static uint Desc(OrigMem m, uint x, int n, int i)   // 0x081108C0
        {
            if (n == 1) return m.R16(G(x) + 0x16 + 2 * (uint)i);
            for (uint k = 0; k < 4; k++)
            {
                uint c = m.R16(G(x) + 0x16 + 2 * k);
                if (InHouse(m, c) != None) return Desc(m, c, n - 1, i);
            }
            return None;
        }

        static uint SpouseOf(OrigMem m, uint x, int n, uint g)   // 0x081109C0
        {
            if (n == 0)
            {
                uint sp = m.R16(G(x) + 0x14);
                if (g == 2) return sp;
                return m.R8(G(sp) + 0x31) == g ? sp : None;
            }
            for (uint k = 0; k < 4; k++)
            {
                uint c = m.R16(G(x) + 0x16 + 2 * k);
                if (InHouse(m, c) != None) return SpouseOf(m, c, n - 1, g);
            }
            return None;
        }

        static int FirstChildInHouse(OrigMem m, uint x)     // 0x08111384
        {
            for (uint k = 0; k < 4; k++)
                if (InHouse(m, m.R16(G(x) + 0x16 + 2 * k)) != None) return (int)k;
            return 4;
        }

        static void S(OrigMem m, int k, uint v) { m.W16(OrigMem.Slots + 2 * (uint)k, v); }
        public static uint Slot(OrigMem m, int k) { return m.R16(OrigMem.Slots + 2 * (uint)k); }

        /// <summary>0x08110B90(번호, 0): 그 사람을 중심으로 관계 슬롯표를 다시 만든다.</summary>
        public static void BuildSlots(OrigMem m, uint x)
        {
            for (int k = 0; k < 23; k++) S(m, k, None);
            S(m, 0, x);
            S(m, 1, AncM(m, x, 3)); S(m, 2, AncF(m, x, 3));
            S(m, 3, AncM(m, x, 2)); S(m, 4, AncF(m, x, 2));
            S(m, 5, AncM(m, x, 1)); S(m, 6, AncF(m, x, 1));
            S(m, 15, SpouseOf(m, x, 0, 2));
            for (int i = 0; i < 4; i++) S(m, 7 + i, Desc(m, x, 1, i));
            for (int i = 0; i < 4; i++) S(m, 11 + i, Desc(m, x, 2, i));
            S(m, 16, SpouseOf(m, x, 1, 2)); S(m, 17, SpouseOf(m, x, 2, 2));
            int fc = FirstChildInHouse(m, x);
            if (fc != 4)
            {
                uint c = m.R16(G(x) + 0x16 + 2 * (uint)fc);
                if (m.R8(G(c) + 0x31) == 0) { S(m, 18, Slot(m, 16)); S(m, 19, c); }
                else { S(m, 18, c); S(m, 19, Slot(m, 16)); }
            }
            uint f = m.R16(G(x) + 0x10), mo = m.R16(G(x) + 0x12);
            if (f != None || mo != None)
            {
                uint p = f != None ? G(f) : G(mo); int j = 0;
                for (uint i = 0; i < 4; i++)
                {
                    uint c = m.R16(p + 0x16 + 2 * i);
                    if (c == None) break;
                    if (c != x) { S(m, 20 + j, c); j++; }
                }
            }
            S(m, 27, m.R16(OrigMem.Fam + 0x66C));
        }

        /// <summary>0x08110B2C: 번호 → 가족 레코드 주소 (함께 사는 8명 중에 없으면 0).</summary>
        public static uint Record(OrigMem m, uint x)
        {
            if (x == None) return 0;
            for (int n = 0; n < 8; n++)
                if (m.R16(OrigMem.PersonAddr(n) + 0x3C) == x) return OrigMem.PersonAddr(n);
            return 0;
        }

        /// <summary>0x08115778: 슬롯 slot 의 사람을 중심으로 했을 때 관계 k 의 사람이 가족 안에 있는가 (없는 슬롯이면 -1).</summary>
        public static uint Rel(OrigMem m, int slot, int k)
        {
            uint target = Slot(m, slot);
            if (target == None) return 0xFFFFFFFF;
            uint saved = Slot(m, 0);
            BuildSlots(m, target);
            uint res;
            if (k == 0x1C) res = m.R8(Record(m, Slot(m, 0)) + 0x74) != 0xFF ? 1u : 0u;
            else if (k == 0x0F)
            {
                uint r = Record(m, Slot(m, 0));
                if (r == 0) r = G(Slot(m, 0));
                res = Record(m, m.R16(r + 0x14)) != 0 ? 1u : 0u;
            }
            else res = Record(m, Slot(m, k)) != 0 ? 1u : 0u;
            BuildSlots(m, saved);
            return res;
        }
    }
}

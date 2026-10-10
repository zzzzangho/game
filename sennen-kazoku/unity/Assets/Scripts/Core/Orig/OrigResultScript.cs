using System;

namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 사건 결과 스크립트 (원작 글 상자 엔진 0x080A7xxx 의 "1A 12" 명령 → 해석기 0x080ABA58).
    /// 사건 대사 끝의 1A 12 00 이 결과 기록 +0x10 의 결과 스크립트로 넘어가고, 그 안의 1A 12 xx 명령이
    /// 결과 표시·감사(신님에게 감사, 감사의 마음 0x0202C670)·신님 랭크(0x0202C66E)·랭크 보상(화살·아이템)을 처리한다.
    /// 실기 중단점 덤프로 확인한 글 상자 객체 T: +0 인물 번호, +4 = 0xFFFFFFFF, +0x10 프레임 수, +0x2C 결과 기록,
    /// 프레임 k = T + 0x74 + 0xA0·k (+0 글 포인터, +4 읽는 위치), 명령이 만든 글은 T 안 버퍼(+0x134~)에 써서 새 프레임으로 넣는다.
    ///
    /// 여기서 하는 일은 원작 글 상자 엔진에서 "글자를 그리는 것"만 뺀 것이다: 글을 읽어 나가다
    /// 1A 12 면 해석기 명령을 원작 함수(변환 트리)로 실행하고, 1A FF 면 프레임을 닫고, 나머지 토큰·글자는 건너뛴다.
    /// 해석기 분기 표(0x080ABAF4, 명령 0~0x14)는 바이트를 읽고 함수를 고르는 일만 해서 C# 로 옮겼다.
    /// 명령 2(0x080ABF74, "대상마다 반복" 시작)는 k 번째 비트를 찾는 반복이 데이터에 따라 달라 변환기가 다루지 못해 손 이식했다(Op2).
    /// 명령 3(0x080AC560)이 T+0x12 를 줄이며 반복 시작 표시(프레임+0x80)로 되돌린다.
    /// </summary>
    public static class OrigResultScript
    {
        /// <summary>글 상자 객체 자리 (작업 영역 — 기록 범위 0x0F000000~0x0F03FFFF 안, 비교 범위 밖).</summary>
        public const uint T = 0x0F03A000, TSize = 0x1000;

        static readonly string[] Handlers =
        {
            "080ABBDC", "080ABC34", null /* 0x080ABF74 표시 전용 */, "080AC560", "080ABE2C", "080AC670", "080ACCA4", "080ADA6C",
            "080AE0AC", "080AE368", "080AE690", "080AE974", "080AEAFC", "080AEEB4", "080ABD54", "080ABDC0",
            "080AF06C", "080ABD54", "080AE690", "080AE974", "080AEAFC",
        };

        /// <summary>글 상자 토큰 1A xx 의 길이 (참고 대사의 토큰 길이로 정함, 1A 0E 는 하위 명령에 따라). 모르는 것은 -1.</summary>
        static int TokenLen(OrigMem m, uint p)
        {
            uint op = m.R8(p + 1);
            switch (op)
            {
                case 0x01: case 0x02: case 0x09: case 0x0D: return 2;
                case 0x03: case 0x0A: return 3;
                case 0x05: case 0x06: case 0x08: case 0x0B: case 0x0F: case 0x86: return 4;   // 1A 86 = 앱의 이름 표지(NameMarks). 1A 05 = 숫자 글 (0x080AB774, ROM 글에서 길이 확인)
                case 0x10: return 5;
                case 0x0E:
                    {
                        uint sub = m.R8(p + 2);
                        return sub == 0 ? 4 : sub == 1 ? 7 : sub == 2 ? 5 : 2;
                    }
                default: return -1;
            }
        }

        /// <summary>
        /// 이름 표지: 원작 글 엔진은 이름을 메모리의 이름 칸(족보 +0x20 14바이트, 가문 이름 0x0202C6A0, 플레이어 이름 0x0202C660)에서
        /// 복사하거나(0x080A4FD8, 0 바이트 앞까지) 그 칸을 바로 글 프레임으로 쓴다. 앱은 한국식 이름을 따로 가지므로(OrigNames)
        /// Run 동안 이 칸들에 "1A 86 b1 b2 00" (앱만 쓰는 토큰 — 번호 14비트를 7비트씩 0x80 을 더해 넣어 0 바이트가 없게 한다.
        /// 0x3FFD 가문, 0x3FFE 플레이어, 그 밖 족보 번호)을 넣었다가 끝나면 되돌린다. 풀기는 MarkId.
        /// 글 상자는 이 토큰을 그대로 shown 에 넘기고, OrigText.Decode 가 앱 이름으로 바꾼다.
        /// </summary>
        public const uint FamilyNameAddr = 0x0202C6A0, PlayerNameAddr = 0x0202C660, MarkFamily = 0x3FFD, MarkPlayer = 0x3FFE;

        /// <summary>이름 표지 두 바이트 → 번호.</summary>
        public static int MarkId(int b1, int b2) { return (b1 & 0x7F) | (b2 & 0x7F) << 7; }

        static System.Collections.Generic.List<(uint addr, byte[] old)> NameMarks(OrigMem m, uint personId)
        {
            var saved = new System.Collections.Generic.List<(uint, byte[])>();
            var done = new System.Collections.Generic.HashSet<uint>();
            void Mark(uint addr, uint pid)
            {
                if (!done.Add(addr)) return;
                var old = new byte[5]; for (uint i = 0; i < 5; i++) old[i] = (byte)m.R8(addr + i);
                saved.Add((addr, old));
                m.W8(addr, 0x1A); m.W8(addr + 1, 0x86); m.W8(addr + 2, (pid & 0x7F) | 0x80); m.W8(addr + 3, ((pid >> 7) & 0x7F) | 0x80); m.W8(addr + 4, 0);
            }
            void Person(uint id) { if (id < MarkFamily) Mark(OrigMem.GeneAddr(id) + 0x20, id); }
            Person(personId & 0xFFFF);
            for (uint k = 0; k < 28; k++) Person(m.R16(OrigMem.Slots + 2 * k));
            for (int n = 0; n < 8; n++) if (OrigGame.Present(m, n)) Person(m.R16(OrigMem.PersonAddr(n) + 0x3C));
            Mark(FamilyNameAddr, MarkFamily); Mark(PlayerNameAddr, MarkPlayer);
            return saved;
        }

        /// <summary>
        /// 명령 2 손 이식 (원작 0x080ABF74). fr = 지금 프레임(명령 바이트 다음을 가리킴).
        /// 처음(T+0x12 = 0): 반복 시작 표시 +0x80 = 위치 − 3, 인자 a:
        ///   0x23/0x24/0x25 → 묶음 비트 T+0x30 이 0 이면 T+0x12 = 1, T+4 = 0x14/7/0xE.
        ///   아니면 T+0x12 = 비트 0~2(0x23) 또는 0~3(0x24·0x25) 중 선 개수, T+4 = 0x17/0xB/0xF − j
        ///   (j = T+0x12 번째로 선 비트의 자리 + 1). 그 밖의 인자 → T+4 = a, T+0x12 = 1.
        /// 다시(명령 3 이 되돌림): 인자를 건너뛰고 T+4 를 묶음 첫 칸(0x16/0xE/0xA)으로 돌린 뒤 + 1 − j.
        /// 끝으로 머리글 프레임을 하나 쌓는다(아래 머리글 참고).
        /// </summary>
        static void Op2(OrigMem m, uint fr)
        {
            uint ix = m.R32(fr + 4);
            uint bits = m.R8(T + 0x30);
            bool argWas22 = m.R8(m.R32(fr) + ix) == 0x22;   // 처음이든 다시든 ix 가 인자 바이트를 가리킨다
            int Pos(uint count)   // count 번째로 선 비트 자리 + 1 (원작 반복 그대로: 0 이면 0)
            {
                uint c = 0; int j = 0;
                while (c != count) { if (j >= 32) throw new OrigUnmodeled("결과 스크립트 명령 2: 묶음 비트 부족"); if (((bits >> j) & 1) != 0) c++; j++; }
                return j;
            }
            if (m.R8(T + 0x12) == 0)
            {
                m.W32(fr + 0x0C, ix - 3);   // 프레임 +0x80 (프레임이 T+0x74 에서 시작하므로 프레임 기준 +0x0C)
                uint a = m.R8(m.R32(fr) + ix); m.W32(fr + 4, ix + 1);
                if (a == 0x23 || a == 0x24 || a == 0x25)
                {
                    uint first = a == 0x23 ? 0x14u : a == 0x24 ? 7u : 0xEu, top = a == 0x23 ? 0x17u : a == 0x24 ? 0xBu : 0xFu;
                    if (bits == 0) { m.W8(T + 0x12, 1); m.W32(T + 4, first); }
                    else
                    {
                        uint n = 0, lim = a == 0x23 ? 2u : 3u;
                        for (int k = 0; k <= lim; k++) if (((bits >> k) & 1) != 0) n++;
                        m.W8(T + 0x12, n);
                        m.W32(T + 4, (uint)(top - Pos(n)));
                    }
                }
                else { m.W32(T + 4, a); m.W8(T + 0x12, 1); }
            }
            else
            {
                m.W32(fr + 4, ix + 1);
                uint t4 = m.R32(T + 4);
                t4 = t4 > 0x13 ? 0x16u : t4 > 0xA ? 0xEu : 0xAu;
                int j = Pos(m.R8(T + 0x12));
                if (j != 0) t4 = (uint)(t4 + 1 - j);
                m.W32(T + 4, t4);
            }
            // 머리글 (0x080AC414~): 글 표 0x085C081C 의 +0x314 (머리글 앞부분) 뒤에, 인자 0x22 면 +0x31C (가족 전체 문구),
            // 아니면 색 1A 03 0C · 대상 이름(0x080A662C: 슬롯표[T+4] 의 족보 +0x20, 빈 칸이면 +0xF4 기본 글) · 색 1A 03 0A · +0x318 (조사 문구).
            // 글 복사(0x080A4FD8)는 0 바이트 앞까지라 +0x318 / +0x31C 는 "… 1A 12 04" 에서 끊기고, 0x080A52C0 이 "00 00 1A FF" 를 붙인다.
            uint cnt = m.R8(T + 0x10), nf = T + 0x74 + 0xA0 * cnt, buf = nf + 0x20, w = buf;
            void Copy(uint src) { for (uint b; (b = m.R8(src)) != 0; src++) m.W8(w++, b); }
            void Put(params byte[] bs) { foreach (var b in bs) m.W8(w++, b); }
            uint slot = m.R32(T + 4);
            Copy(m.R32(HeadStrings + 0x314));
            if (argWas22) Copy(m.R32(HeadStrings + 0x31C));
            else
            {
                Put(0x1A, 0x03, 0x0C);
                uint id = slot < 28 ? m.R16(OrigMem.Slots + 2 * slot) : 0xFFFF;
                if (id != 0xFFFF) Copy(OrigMem.GeneAddr(id) + 0x20);   // Run 동안은 이름 표지(NameMarks)
                else Copy(m.R32(HeadStrings + 0xF4));
                Put(0x1A, 0x03, 0x0A);
                Copy(m.R32(HeadStrings + 0x318));
            }
            Put(0x00, 0x00, 0x1A, 0xFF);
            m.W32(nf, buf); m.W32(nf + 4, 0); m.W8(T + 0x10, cnt + 1);
        }

        /// <summary>명령 2 머리글의 글 표 (0x080AC44C 의 상수).</summary>
        const uint HeadStrings = 0x085C081C;

        /// <summary>사건 하나의 결과 스크립트를 실행한다 (personId = 사건 인물, record = 결과 기록). 실행한 해석기 명령 수를 돌려준다.</summary>
        /// <param name="shown">있으면 글 상자가 화면에 낼 글 바이트를 차례대로 담는다(글자, 줄·장 바꿈 1A 01/02/09, 이름 1A 06 → 1A 86 족보 번호, 숫자 1A 05).
        /// 원작 글자표로 푸는 것은 OrigText.Decode.</param>
        public static int Run(OrigVm vm, uint personId, uint record, System.Collections.Generic.List<byte> shown = null)
        {
            var m = vm.Mem;
            for (uint i = 0; i < TSize; i += 4) m.W32(T + i, 0);
            m.W32(T, personId); m.W32(T + 4, 0xFFFFFFFF); m.W32(T + 0x2C, record);
            int ops = 1; var hist = new System.Collections.Generic.List<string>();
            var trEnv = Environment.GetEnvironmentVariable("SK_RS_TRACE"); bool trace = trEnv != null && (trEnv == "1" || trEnv == record.ToString("X8")); uint lastN = 1;
            var marks = NameMarks(m, personId);
            try { return RunFrames(vm, personId, record, shown, ops, hist, trace, lastN); }
            finally { foreach (var mk in marks) for (uint i = 0; i < 5; i++) m.W8(mk.addr + i, mk.old[i]); }
        }

        static int RunFrames(OrigVm vm, uint personId, uint record, System.Collections.Generic.List<byte> shown, int ops, System.Collections.Generic.List<string> hist, bool trace, uint lastN)
        {
            var m = vm.Mem;
            vm.Call("080ABBDC", T);   // 명령 0: 프레임 0 = 결과 기록 +0x10 의 결과 스크립트
            if (trace) { uint pr = vm.Call("08110B2C", personId & 0xFFFF); Console.WriteLine("    [RS] 시작 인물 " + personId + " 레코드 " + pr.ToString("X8") + " +0x69=" + m.R8(pr + 0x69) + " +0x60=" + m.R8(pr + 0x60) + " 감사의 마음 " + m.R32(0x0202C670)); }
            for (int guard = 0; guard < 20000; guard++)
            {
                uint n = m.R8(T + 0x10);
                if (n == 0) { if (trace) Console.WriteLine("    [RS] 끝: 감사의 마음 " + m.R32(0x0202C670) + " 랭크 " + m.R16(0x0202C66E)); return ops; }
                uint fr = T + 0x74 + 0xA0 * (n - 1);
                uint p = m.R32(fr), ix = m.R32(fr + 4), a = p + ix;
                if (trace && n != lastN)
                {
                    var sb = new System.Text.StringBuilder(); for (uint q = 0; q < 24; q++) sb.Append(m.R8(a + q).ToString("X2")).Append(' ');
                    Console.WriteLine("    [RS] 프레임 " + lastN + "→" + n + " " + p.ToString("X8") + "+" + ix + ": " + sb); lastN = n;
                }
                uint b = m.R8(a);
                if (b == 0 && m.R32(fr + 8) != 0) { m.W8(T + 0x10, n - 1); continue; }   // 0 바이트: 프레임 +8 이 0 이 아니면 프레임 닫기(0x080A6852), 아니면 건너뛰기
                if (b != 0x1A)
                {
                    if (shown != null && b != 0) { shown.Add((byte)b); if (b >= 0x80) shown.Add((byte)m.R8(a + 1)); }
                    m.W32(fr + 4, ix + (b >= 0x80 ? 2u : 1u)); continue;
                }
                uint op = m.R8(a + 1);
                if (op == 0xFF) { m.W8(T + 0x10, n - 1); continue; }
                if (op == 0x12)
                {
                    uint iop = m.R8(a + 2);
                    hist.Add(n + ":" + p.ToString("X8") + "+" + ix + "=" + iop.ToString("X2")); if (hist.Count > 24) hist.RemoveAt(0);
                    m.W32(fr + 4, ix + 3);
                    if (iop < Handlers.Length)
                    {
                        if (iop == 2) { Op2(m, fr); ops++; }
                        else if (Handlers[iop] != null) { vm.Call(Handlers[iop], T); ops++; if (trace) Console.WriteLine("    [RS] 명령 " + iop.ToString("X2") + " @" + n + ":" + p.ToString("X8") + "+" + ix + " → 프레임 수 " + m.R8(T + 0x10) + ", 프레임0 위치 " + m.R32(T + 0x78)); }
                    }
                    continue;
                }
                // 숫자 1A 05 (0x080AB774) · 값 자리 1A 06 (0x080A796C) 는 원작 글 엔진(토큰 분기 0x080A7138)처럼 처리 함수를 불러
                // 글을 새 프레임으로 쌓는다. 이름은 원작 메모리의 이름 칸에서 오므로 Run 동안 이름 칸에 표지를 넣어 둔다(NameMarks).
                if (op == 0x05 || op == 0x06)
                {
                    m.W32(fr + 4, ix + 2);
                    if (Environment.GetEnvironmentVariable("SK_TOK_TRACE") != null) Console.WriteLine("    [TOK] 1A " + op.ToString("X2") + " " + m.R8(a + 2).ToString("X2") + " " + m.R8(a + 3).ToString("X2") + " @" + record.ToString("X8"));
                    vm.Call(op == 0x05 ? "080AB774" : "080A796C", T);
                    continue;
                }
                int len = TokenLen(m, a);
                if (len < 0) throw new OrigUnmodeled("결과 스크립트의 모르는 글 토큰 1A " + op.ToString("X2") + " (" + a.ToString("X8") + ")");
                if (shown != null && (op == 0x01 || op == 0x02 || op == 0x09 || op == 0x86))
                    for (uint q = 0; q < len; q++) shown.Add((byte)m.R8(a + q));
                m.W32(fr + 4, ix + (uint)len);
            }
            throw new OrigUnmodeled("결과 스크립트가 끝나지 않음 (" + record.ToString("X8") + ") 마지막 명령 " + string.Join(" ", hist));
        }
    }
}

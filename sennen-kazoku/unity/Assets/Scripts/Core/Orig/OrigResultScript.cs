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
                case 0x05: case 0x06: case 0x08: case 0x0B: case 0x0F: return 4;   // 1A 05 = 숫자 글 (0x080AB774, ROM 글에서 길이 확인)
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
        /// 명령 2 손 이식 (원작 0x080ABF74). fr = 지금 프레임(명령 바이트 다음을 가리킴).
        /// 처음(T+0x12 = 0): 반복 시작 표시 +0x80 = 위치 − 3, 인자 a:
        ///   0x23/0x24/0x25 → 묶음 비트 T+0x30 이 0 이면 T+0x12 = 1, T+4 = 0x14/7/0xE.
        ///   아니면 T+0x12 = 비트 0~2(0x23) 또는 0~3(0x24·0x25) 중 선 개수, T+4 = 0x17/0xB/0xF − j
        ///   (j = T+0x12 번째로 선 비트의 자리 + 1). 그 밖의 인자 → T+4 = a, T+0x12 = 1.
        /// 다시(명령 3 이 되돌림): 인자를 건너뛰고 T+4 를 묶음 첫 칸(0x16/0xE/0xA)으로 돌린 뒤 + 1 − j.
        /// 끝으로 머리글 프레임을 하나 쌓는다(원작은 이름 글 + "1A 12 04 00 00"; 앱은 글을 빼고 끝 명령만 넣는다).
        /// </summary>
        static void Op2(OrigMem m, uint fr)
        {
            uint ix = m.R32(fr + 4);
            uint bits = m.R8(T + 0x30);
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
            uint cnt = m.R8(T + 0x10), nf = T + 0x74 + 0xA0 * cnt, buf = nf + 0x20;
            byte[] tail = { 0x1A, 0x12, 0x04, 0x00, 0x00, 0x1A, 0xFF };
            for (uint i = 0; i < tail.Length; i++) m.W8(buf + i, tail[i]);
            m.W32(nf, buf); m.W32(nf + 4, 0); m.W8(T + 0x10, cnt + 1);
        }

        /// <summary>사건 하나의 결과 스크립트를 실행한다 (personId = 사건 인물, record = 결과 기록). 실행한 해석기 명령 수를 돌려준다.</summary>
        public static int Run(OrigVm vm, uint personId, uint record)
        {
            var m = vm.Mem;
            for (uint i = 0; i < TSize; i += 4) m.W32(T + i, 0);
            m.W32(T, personId); m.W32(T + 4, 0xFFFFFFFF); m.W32(T + 0x2C, record);
            int ops = 1; var hist = new System.Collections.Generic.List<string>();
            var trEnv = Environment.GetEnvironmentVariable("SK_RS_TRACE"); bool trace = trEnv != null && (trEnv == "1" || trEnv == record.ToString("X8")); uint lastN = 1;
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
                if (b != 0x1A) { m.W32(fr + 4, ix + (b >= 0x80 ? 2u : 1u)); continue; }
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
                int len = TokenLen(m, a);
                if (len < 0) throw new OrigUnmodeled("결과 스크립트의 모르는 글 토큰 1A " + op.ToString("X2") + " (" + a.ToString("X8") + ")");
                m.W32(fr + 4, ix + (uint)len);
            }
            throw new OrigUnmodeled("결과 스크립트가 끝나지 않음 (" + record.ToString("X8") + ") 마지막 명령 " + string.Join(" ", hist));
        }
    }
}

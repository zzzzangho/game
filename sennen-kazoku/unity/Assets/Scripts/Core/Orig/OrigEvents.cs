namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 원작 MAX/MIN 사건 진행의 손 이식.
    /// - 변형 고르기 0x08119CA0: 판정 함수가 참인 첫 변형, 하나도 없으면 목록의 n 번째(끝 다음) 항목.
    /// - 사건이 끝날 때 결과 기록 +0x1C 효과 함수를 부른다(0x0804D9B2). 효과 함수는 변환한 트리로 실행된다
    ///   (능력치 변화·몰입도 보정·다음 관심사 등 원작 코드 그대로). 시작 함수(+0x18)는 대사 문장 준비라 쓰지 않는다.
    /// 판정·효과 전에 관계 슬롯표를 사건 인물 중심으로 만든다(원작 사건 시작과 같음).
    /// </summary>
    public static class OrigEvents
    {
        public const uint ResultBuf = 0x03007D00;   // 효과 함수가 결과 3단어를 써 넣는 곳 (원작: 호출한 쪽 지역 변수)

        /// <summary>가족 레코드 n 의 지금 관심사가 MAX(255)/MIN(0) 일 때 일어날 사건 항목 주소 (없으면 0).</summary>
        public static uint EventOf(OrigMem m, int n, bool max)
        {
            uint p = OrigMem.PersonAddr(n);
            uint t = m.R16(p + 0x80), i = m.R16(p + 0x82);
            uint entry = m.R32(m.R32(OrigSelect.Entries + 4 * t) + 4 * i);
            return m.R32(entry + (max ? 0x1Cu : 0x20u));
        }

        /// <summary>0x08119CA0: 변형 번호 (0..n).</summary>
        public static int PickVariant(OrigVm vm, OrigRules rules, uint ev)
        {
            if (!rules.Events.TryGetValue(ev, out var e)) throw new OrigUnmodeled("팩에 없는 사건 " + ev.ToString("X8"));
            for (int i = 0; i < e.Count; i++)
                if (e.Preds[i] != null && vm.Call(e.Preds[i], e.List + 4 * (uint)i) != 0) return i;
            return e.Count;
        }

        /// <summary>결과 기록의 효과 함수 실행. arg = 시작 함수가 돌려준 값(사건 장면 +0x286). 돌려주는 값 = 결과 3단어.</summary>
        /// <summary>결과 기록의 시작(+0x18)·효과(+0x1C) 함수. 사건 변형 목록에서 모은 표에 없으면(일정·가족 일정에서 온 기록 등) ROM 기록에서 직접 읽는다.</summary>
        public static string[] Fns(OrigVm vm, OrigRules rules, uint data)
        {
            if (rules.VariantData.TryGetValue(data, out var fns)) return fns;
            string F(uint f) { return (f & 1) != 0 && f >= 0x08000000 && f < 0x08300000 ? "0x" + f.ToString("X8") : null; }
            return new[] { F(vm.Mem.R32(data + 0x18)), F(vm.Mem.R32(data + 0x1C)) };
        }

        public static uint[] RunEffect(OrigVm vm, OrigRules rules, uint data, uint arg = 0)
        {
            var fns = Fns(vm, rules, data);
            if (fns[1] == null) return new uint[3];
            vm.Call(fns[1], ResultBuf, arg & 0xFF, 0, 0);
            return new[] { vm.Mem.R32(ResultBuf), vm.Mem.R32(ResultBuf + 4), vm.Mem.R32(ResultBuf + 8) };
        }

        /// <summary>
        /// 사건 장면 한 번 (0x0801E724 장면의 규칙 부분):
        /// 시작 함수 +0x18 (0x0804C84A, 돌려준 값 → 장면 +0x286) → 참가 슬롯 0x08110F5C(그 값) → 효과 함수 +0x1C(결과, 그 값) (0x0804D9B2)
        /// → 메인 장면 복귀 0x080111B8(-3, 인물 번호, 1, ·, 그 값, ·, 결과 기록): 추억 기록·후속 일정·날 넘김.
        /// 시작 함수 5,311개 중 대부분은 0 을 돌려준다(값을 계산하는 것 약 170개).
        /// </summary>
        public static uint RunScene(OrigVm vm, OrigRules rules, uint data, uint personId) { return RunScene(vm, rules, data, personId, null); }

        /// <param name="slotsOut">있으면 장면 시작 때 만든 슬롯표(28칸)를 담는다 — 대사의 인물 이름 자리(제어 토큰 1A 06)를 채울 때 쓴다.</param>
        public static uint RunScene(OrigVm vm, OrigRules rules, uint data, uint personId, uint[] slotsOut) { return RunScene(vm, rules, data, personId, slotsOut, null); }

        /// <param name="resultOut">있으면 효과 함수의 결과 3단어(원작은 장면 +0x288 에 옮긴다). 첫 단어 = 장면 끝 종류 (0x0804DB9C~):
        /// 1 메인 장면(종류 3), 2 가문이 끊긴 장면(0x0809AF60(5,2) → 0x0809EAAC), 3 장면 종류 4, 4 → 끝 종류 7, 그 밖 → 보통 복귀 0x080111B8.</param>
        public static uint RunScene(OrigVm vm, OrigRules rules, uint data, uint personId, uint[] slotsOut, uint[] resultOut) { return RunScene(vm, rules, data, personId, slotsOut, resultOut, null); }

        /// <param name="shownOut">있으면 결과 스크립트가 글 상자에 낼 글 바이트(OrigResultScript.Run 의 shown).</param>
        public static uint RunScene(OrigVm vm, OrigRules rules, uint data, uint personId, uint[] slotsOut, uint[] resultOut, System.Collections.Generic.List<byte> shownOut) { return RunScene(vm, rules, data, personId, slotsOut, resultOut, shownOut, null); }

        /// <param name="textOverride">글 주소 → 앱 번역으로 바꿔 끼울 글인가 (OrigResultScript.Run 참고).</param>
        public static uint RunScene(OrigVm vm, OrigRules rules, uint data, uint personId, uint[] slotsOut, uint[] resultOut, System.Collections.Generic.List<byte> shownOut, System.Func<uint, bool> textOverride)
        {
            // 장면 시작 0x0804C66C 의 규칙 부분 (원작 실행 중단점으로 확인: 시작 함수 전에 0x0804C76C 에서 슬롯표를 다시 만든다):
            // 0x0800E57C — 인물 레코드 앞 0x38 바이트를 가계 표(0x0202EB9C + 60×번호)에 옮겨 적기,
            // 0x08110B90(사건 인물, 0) — 사건 인물 중심 슬롯표. 큐에 넣은 뒤 다른 인물 처리로 슬롯표가 바뀌었어도 여기서 바로잡힌다.
            bool tr = System.Environment.GetEnvironmentVariable("SK_RS_TRACE") == data.ToString("X8");
            uint pr0 = tr ? vm.Call("08110B2C", personId & 0xFFFF) : 0;
            if (tr) System.Console.WriteLine("    [SC] 장면 시작 +0x69=" + vm.Mem.R8(pr0 + 0x69) + " 게이지 " + vm.Mem.R8(pr0 + 0x5A));
            vm.Call("0800E57C");
            vm.Call("slots", personId, 0);
            // 장면 +0x29C = 장면 시작 때 신님 랭크(0x0804C754), +0x2A0 = 사건 인물 레코드 +0x69(화살 표시, 0x0804C7AE — 인물이 없으면 적지 않는다)
            uint rank0 = vm.Mem.R16(OrigMem.Fam + 0x65E), pn = vm.Call("0800E524", personId & 0xFFFF);
            uint arrow = pn < 8 ? vm.Mem.R8(OrigMem.PersonAddr((int)pn) + 0x69) : 0;
            if (slotsOut != null) for (int k = 0; k < slotsOut.Length && k < 28; k++) slotsOut[k] = vm.Mem.R16(OrigMem.Slots + 2 * (uint)k);
            uint v = 0;
            var fns = Fns(vm, rules, data);
            if (fns[0] != null) v = vm.Call(fns[0], 0, data, 0, 0) & 0xFFFF;
            if (tr) System.Console.WriteLine("    [SC] 시작 함수 뒤 +0x69=" + vm.Mem.R8(pr0 + 0x69));
            vm.Call("08110F5C", v & 0xFF);
            // 대사 끝의 1A 12 00 → 결과 스크립트(감사·신님 랭크·랭크 보상). 원작은 장면에서 대사를 보여 주며 실행하고,
            // 효과 함수는 그 뒤 장면 끝에서 돈다 — 실기 감시: 대사 끝(명령 0) 7811 → 감사 8051 → 랭크·보상 8241 → 화살 표시 해제(효과 쪽 0x0802886A) 9560 프레임.
            OrigResultScript.Run(vm, personId, data, shownOut, textOverride);
            var res = RunEffect(vm, rules, data, v);
            if (resultOut != null) for (int k = 0; k < 3 && k < resultOut.Length; k++) resultOut[k] = res[k];
            CupidHelp(vm.Mem, data, arrow, rank0);
            if (tr) System.Console.WriteLine("    [SC] 효과 뒤 +0x69=" + vm.Mem.R8(pr0 + 0x69));
            vm.Call("080111B8", 0xFFFFFFFD, personId, 1, 0, v, 0, data);
            return v;
        }

        public const uint HelpQueue = 0x0203BD30;

        /// <summary>
        /// 사건 장면 끝 큐피트 도움말 (0x0804DB50, 장면 +0x54 = 0 인 큐 사건). 결과 기록 +0 종류가 6~10 일 때, 가족 +0x24 비트로 한 번씩:
        /// 화살 표시 비트 0 이면 도움말 1(비트 1) — 이미 봤으면 도움말 2(비트 2); 랭크가 장면 동안 1→2 넘으면 3(비트 4), 2→3 은 4(비트 8),
        /// 3→4 는 5(비트 0x10), 4→5 는 6(비트 0x20). 넣기 = 0x0802B1CC (3칸 큐를 한 칸 밀고 맨 앞에).
        /// 실기 사건 장면 덤프: 화살 맞은 인물의 종류 8 사건 뒤 0x0203BD30 = 01 FF FF, 가족 +0x24 = 1.
        /// </summary>
        public static void CupidHelp(OrigMem m, uint data, uint arrow, uint rank0)
        {
            uint kind = m.R8(data);
            if (kind < 6 || kind > 10) return;
            uint rank = m.R16(OrigMem.Fam + 0x65E), f = m.R8(OrigMem.Fam + 0x24);
            if ((f & 1) == 0 && (arrow & 1) != 0) { PushHelp(m, 1); f |= 1; }
            else if ((f & 2) == 0 && (arrow & 1) != 0) { PushHelp(m, 2); f |= 2; }
            if ((f & 4) == 0 && rank0 <= 1 && rank > 1) { PushHelp(m, 3); f |= 4; }
            if ((f & 8) == 0 && rank0 <= 2 && rank > 2) { PushHelp(m, 4); f |= 8; }
            if ((f & 0x10) == 0 && rank0 <= 3 && rank > 3) { PushHelp(m, 5); f |= 0x10; }
            if ((f & 0x20) == 0 && rank0 <= 4 && rank > 4) { PushHelp(m, 6); f |= 0x20; }
            m.W8(OrigMem.Fam + 0x24, f);
        }

        static void PushHelp(OrigMem m, uint k)
        {
            m.W8(HelpQueue + 2, m.R8(HelpQueue + 1)); m.W8(HelpQueue + 1, m.R8(HelpQueue)); m.W8(HelpQueue, k);
        }

        /// <summary>0x0802B22C: 큐 끝(오래된 것)부터 꺼낸다. 없으면 −1.</summary>
        public static int PopHelp(OrigMem m)
        {
            for (uint i = 3; i > 0; i--)
            {
                uint v = m.R8(HelpQueue + i - 1);
                if (v != 0xFF) { m.W8(HelpQueue + i - 1, 0xFF); return (int)v; }
            }
            return -1;
        }

        /// <summary>가족 레코드 n 의 사건 한 번: 슬롯표 → 변형 → 효과. 돌려주는 값 = 결과 기록 주소.</summary>
        public static uint Run(OrigVm vm, OrigRules rules, int n, bool max)
        {
            var m = vm.Mem;
            OrigFamily.BuildSlots(m, m.R16(OrigMem.PersonAddr(n) + 0x3C));
            uint ev = EventOf(m, n, max);
            var e = rules.Events[ev];
            int k = PickVariant(vm, rules, ev);
            uint data = e.Data[k];
            RunEffect(vm, rules, data);
            return data;
        }
    }
}

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
        public static uint[] RunEffect(OrigVm vm, OrigRules rules, uint data, uint arg = 0)
        {
            if (!rules.VariantData.TryGetValue(data, out var fns) || fns[1] == null) return new uint[3];
            vm.Call(fns[1], ResultBuf, arg & 0xFF, 0, 0);
            return new[] { vm.Mem.R32(ResultBuf), vm.Mem.R32(ResultBuf + 4), vm.Mem.R32(ResultBuf + 8) };
        }

        /// <summary>
        /// 사건 장면 한 번 (0x0801E724 장면의 규칙 부분):
        /// 시작 함수 +0x18 (0x0804C84A, 돌려준 값 → 장면 +0x286) → 참가 슬롯 0x08110F5C(그 값) → 효과 함수 +0x1C(결과, 그 값) (0x0804D9B2)
        /// → 메인 장면 복귀 0x080111B8(-3, 인물 번호, 1, ·, 그 값, ·, 결과 기록): 추억 기록·후속 일정·날 넘김.
        /// 시작 함수 5,311개 중 대부분은 0 을 돌려준다(값을 계산하는 것 약 170개).
        /// </summary>
        public static uint RunScene(OrigVm vm, OrigRules rules, uint data, uint personId)
        {
            uint v = 0;
            if (rules.VariantData.TryGetValue(data, out var fns) && fns[0] != null) v = vm.Call(fns[0], 0, data, 0, 0) & 0xFFFF;
            vm.Call("08110F5C", v & 0xFF);
            RunEffect(vm, rules, data, v);
            vm.Call("080111B8", 0xFFFFFFFD, personId, 1, 0, v, 0, data);
            return v;
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

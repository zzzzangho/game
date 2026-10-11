using System;

namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 전원이 꺼진 동안의 진행 (원작 "사건 메모" 장면, 만들기 0x080CC6CC 방식 0 · 시작 0x080C9488 · 하루 묶음 0x080CB74C).
    ///
    /// 원작 순서 (실기 쓰기 감시·실행 중단점으로 확인):
    /// - 기록(큐피트 메뉴 "사건 기록", 0x08092710): 카트리지 시계(BCD)를 검사해 이진수로 가족 +0x67B 년(두 자리) · +0x67C 월 · +0x67D 일 ·
    ///   +0x67E 시 · +0x67F 분 · +0x680 초에 넣고, 가족 +0x20 |= 0x1000, 가족 +0x68C = 난수 한 번(0x08000614).
    /// - 이어 하기(제목 화면 0x08078650 선택 1·2): 난수 seed = 가족 +0x68C(0x080005F8) → 사건 메모 장면 방식 0.
    /// - 시작(방식 0): 지난 시간 = (날짜 차 × 86400 + 시분초 차) >> 3 (0x080D6DA0 — 8초에 하루), 0x598E9 일이 넘으면 0x598E9.
    ///   시작 날짜(+0x5658) = 게임 날짜, 끝 날짜(+0x565C) = 시작 + 일 수(0x08095DAC), 햇수 차(0x080D448C)가 999 를 넘으면 끝 = 시작 + 1000년이고 일 수를 다시 센다.
    ///   그 밖의 장면 칸 초기화와 인물별 칸(+0x9880 + 24·k, 0x0802AFBC) — 아래 Begin.
    /// - 하루 묶음(0x080CB74C, 장면이 프레임마다 부름): 하루씩 진행해 25일마다 0, 끝나거나 멈출 일이 생기면 1 을 돌려준다 — 아래 Step.
    /// 장면 객체는 원작과 같은 EWRAM 0x02001AF0 (새 가족 설정 블록과 같은 자리, 세이브 범위 밖)에 둔다 — 메모 항목이 이 안을 가리키는 포인터를 갖는다.
    /// </summary>
    public sealed class OrigOffline
    {
        public const uint Obj = 0x02001AF0, ObjSize = 0x9964;
        public const uint Fam = OrigMem.Fam;
        public const uint MaxDays = 0x598E9;
        const uint Scratch = 0x0F03C000;   // 원작 함수 지역 변수(인물 칸 번호 · 사건 큐 꺼낸 값 · 지금 시각 구조체) 대신 쓰는 자리

        readonly OrigVm vm; readonly OrigMem m;
        OrigOffline(OrigVm vm) { this.vm = vm; m = vm.Mem; }
        /// <summary>이미 만들어진 장면 객체(실기 덤프 등)에 붙인다.</summary>
        public static OrigOffline Attach(OrigVm vm) { return new OrigOffline(vm); }

        public uint Days { get { return m.R32(Obj + 0x9944); } }
        public uint Done { get { return m.R32(Obj + 0x9940); } }
        public bool Finished { get { return Done >= Days; } }

        /// <summary>기록(0x08092710 의 시계가 바른 경우): 시각을 가족 +0x67B~ 에 넣고 +0x20 |= 0x1000, +0x68C = 난수.</summary>
        public static void Record(OrigMem m, DateTime now)
        {
            m.W8(Fam + 0x67B, (uint)(now.Year % 100)); m.W8(Fam + 0x67C, (uint)now.Month); m.W8(Fam + 0x67D, (uint)now.Day);
            m.W8(Fam + 0x67E, (uint)now.Hour); m.W8(Fam + 0x67F, (uint)now.Minute); m.W8(Fam + 0x680, (uint)now.Second);
            m.W32(Fam + 0x20, m.R32(Fam + 0x20) | 0x1000);
            m.W32(Fam + 0x68C, m.Rand());
        }

        /// <summary>기록한 적이 있는가 (가족 +0x20 비트 0x1000).</summary>
        public static bool Recorded(OrigMem m) { return (m.R32(Fam + 0x20) & 0x1000) != 0; }

        /// <summary>0x080D6DA0: 기록 시각(가족 +0x668 구조체의 +0x13~)부터 지금까지 게임 일 수 = 초 >> 3 (부호 없는 계산 그대로).</summary>
        public static uint ElapsedDays(OrigVm vm, DateTime now)
        {
            var m = vm.Mem; uint saved = Fam + 0x668, a = Scratch + 0x40, b = Scratch + 0x48;
            m.W16(a, m.R8(saved + 0x13)); m.W8(a + 2, m.R8(saved + 0x14)); m.W8(a + 3, m.R8(saved + 0x15));
            m.W16(b, (uint)(now.Year % 100)); m.W8(b + 2, (uint)now.Month); m.W8(b + 3, (uint)now.Day);
            uint days = vm.Call("08095EE8", a, b);
            uint s1 = m.R8(saved + 0x16) * 3600 + m.R8(saved + 0x17) * 60 + m.R8(saved + 0x18);
            uint s2 = (uint)(now.Hour * 3600 + now.Minute * 60 + now.Second);
            return unchecked(days * 86400 + s2 - s1) >> 3;
        }

        /// <summary>이어 하기: 난수 seed = 가족 +0x68C (0x08078650 → 0x080005F8).</summary>
        public static void Reseed(OrigMem m) { m.W32(OrigMem.Seed, m.R32(Fam + 0x68C)); }

        /// <summary>사건 메모 장면 시작(0x080C9488, 방식 0)의 규칙 부분. days = ElapsedDays (원작은 0x0202BFEC 가 0 이 아니면 그 값을 쓴다 — 앱에는 그 경로가 없다).</summary>
        public static OrigOffline Begin(OrigVm vm, uint days)
        {
            var o = new OrigOffline(vm); var m = o.m;
            m.W8(Obj + 8, 0);   // 방식 0 (원작처럼 나머지 칸은 지우지 않는다 — 메모 칸은 사용 표시(+8)만 지운다)
            for (uint k = 0; k < 8; k++) m.W16(Obj + 0x9952 + 2 * k, 0xFFFF);
            m.W32(Obj + 0x38, 0);
            for (uint k = 0; k < 20; k++) m.W32(Obj + 0x5684 + 4 * k, 0);
            m.W32(Obj + 0x5664, m.R16(Fam + 0x66C));
            m.W8(Obj + 0x994B, 0);
            m.W32(Obj + 0x24, 0x46);
            m.W16(Obj + 0x987A, 0);
            m.W8(Obj + 0x994C, 0); m.W8(Obj + 0x994D, 0); m.W8(Obj + 0x994E, 0); m.W8(Obj + 0x994F, 0);
            m.W32(Obj + 0x5668, 0); m.W32(Obj + 0x566C, 0); m.W8(Obj + 0x5670, 1);
            for (uint k = 0; k <= 0x2BB; k++) m.W8(Obj + 0x56DC + 24 * k, 0);
            m.W32(Obj + 0x5680, Obj + 0x5668);
            m.W8(0x02029BA0, 1); m.W16(0x02029BA6, 0);
            m.W32(Obj + 0x9940, 0);
            m.W32(Obj + 0x9944, days);
            vm.Call("0800E430", Obj + 0x5658, OrigMem.Date);
            if (m.R32(Obj + 0x9944) > MaxDays) m.W32(Obj + 0x9944, MaxDays);
            vm.Call("08095DAC", Obj + 0x5658, Obj + 0x565C, m.R32(Obj + 0x9944));
            vm.Call("080D448C", Scratch + 0x20, Scratch + 0x22, Obj + 0x5658, Obj + 0x565C);
            if (m.R16(Scratch + 0x20) > 999)
            {
                m.W16(Obj + 0x565C, m.R16(Obj + 0x5658) + 1000);
                m.W8(Obj + 0x565E, m.R8(Obj + 0x565A)); m.W8(Obj + 0x565F, m.R8(Obj + 0x565B));
                m.W32(Obj + 0x9944, vm.Call("08095EE8", Obj + 0x5658, Obj + 0x565C));
            }
            m.W32(Obj + 0x5660, m.R32(Obj + 0x5658));
            m.W8(Obj + 0x994A, vm.Call("080D4634", Obj));
            m.W32(0x0202BFEC, 0);
            vm.Call("080D7248", Obj);
            m.W16(Obj + 0x9878, (vm.Call("08095D3C", Obj + 0x5658, Obj + 0x565C) + 1) & 0xFFFF);
            m.W16(0x02029BA2, m.R16(Fam + 0x670));
            m.W8(Obj + 0x987C, 0);
            for (uint k = 0; k < 8 && m.R16(OrigMem.Person + OrigMem.PersonSize * k + 0x3C) != 0xFFFF; k++) m.W8(Obj + 0x987C, k + 1);
            for (uint k = 0; k < m.R8(Obj + 0x987C); k++) vm.Call("0802AFBC", OrigMem.Person + OrigMem.PersonSize * k, Obj + 0x9880 + 24 * k);
            return o;
        }

        /// <summary>
        /// 하루 묶음 0x080CB74C 그대로: 하루마다
        ///  ① 월말(날 = 그달 날 수, 0x08096010·0x08096088)이면 인물마다 관계 슬롯표(0x08110B90) 뒤 0x0802A9EC(레코드, 인물 칸, +0x9878), 그다음 0x080D6F38.
        ///  ② 가족 +0x672 = 0x708 이면 0x080D6FC8.  ③ +0x994B = 0 이거나 1월이면 0x080D7534.  ④ 0x02029BA6 = 0 이면 0x080D72F0.
        ///  ⑤ 인물 칸마다(번호 FFFF 는 건너뜀) 관심사 하루 처리 0x08027E78(레코드, 인물 칸) 이 0 이 아니면 0x080D3140(장면, 값, 레코드, 날짜, &amp;칸)
        ///     — 1 이면 +0x38 이 1·2·4 일 때 +0x994C·+0x994D·+0x994F = 1. 0 이 아니면 같은 칸을 다시, 0 이면 다음 칸.
        ///  ⑥ 그날 사건 큐(0x08027D0C → 0x08027BE4) 가 빌 때까지 0x080D75EC.
        ///  ⑦ +0x9940 += 1, 날짜 +1일(0x08095DAC), 게임 날짜에 넣기(0x0800E3C8), 가족 +0x672 = 0x708, +0x994E = 0x080D7710.
        ///  ⑧ 다 됐거나(+0x9940 ≥ +0x9944) +0x994C 면 0x080D7534 뒤 1. +0x994D 면 +0x565C = 오늘 뒤 1. +0x994E 면 +0x38 = 3, +0x565C = 오늘 뒤 1. +0x994F 면 1.
        ///  ⑨ +0x9940 이 25 의 배수면 0, 아니면 다음 날.
        /// </summary>
        public uint Step()
        {
            uint idx = Scratch, buf = Scratch + 8;
            for (;;)
            {
                m.W32(0x0203C268, m.R32(Obj + 0x5660));
                uint leap = vm.Call("08096010", m.R16(Obj + 0x5660)) & 0xFF;
                uint dim = vm.Call("08096088", m.R8(Obj + 0x5662), leap) & 0xFFFF;
                if (m.R8(Obj + 0x5663) == dim)
                {
                    for (uint k = 0; k < m.R8(Obj + 0x987C); k++)
                    {
                        uint rec = OrigMem.Person + OrigMem.PersonSize * k;
                        vm.Call("slots", m.R16(rec + 0x3C), 0);
                        vm.Call("0802A9EC", rec, Obj + 0x9880 + 24 * k, m.R16(Obj + 0x9878));
                    }
                    vm.Call("080D6F38", Obj);
                }
                if (m.R16(Fam + 0x672) == 0x708) vm.Call("080D6FC8", Obj);
                if (m.R8(Obj + 0x994B) == 0 || m.R8(Obj + 0x5662) == 1) vm.Call("080D7534", Obj);
                if (m.R16(0x02029BA6) == 0) vm.Call("080D72F0", Obj);
                m.W8(idx, 0);
                while (m.R8(idx) < m.R8(Obj + 0x987C))
                {
                    uint k = m.R8(idx), rec = OrigMem.Person + OrigMem.PersonSize * k;
                    if (m.R16(rec + 0x3C) == 0xFFFF) { m.W8(idx, k + 1); continue; }
                    uint r = vm.Call("08027E78", rec, Obj + 0x9880 + 24 * k);
                    if (r == 0) { m.W8(idx, k + 1); continue; }
                    uint res = vm.Call("080D3140", Obj, r, rec, Obj + 0x5660, idx) & 0xFF;
                    if (res == 1)
                    {
                        uint t = m.R32(Obj + 0x38);
                        if (t == 1) m.W8(Obj + 0x994C, 1);
                        else if (t == 2) m.W8(Obj + 0x994D, 1);
                        else if (t == 4) m.W8(Obj + 0x994F, 1);
                    }
                }
                for (;;)
                {
                    uint q = vm.Call("08027D0C", Obj + 0x5660);
                    uint n = vm.Call("08027BE4", q, buf) & 0xFF;
                    if (n == 0) break;
                    vm.Call("080D75EC", Obj, buf);
                }
                m.W32(Obj + 0x9940, m.R32(Obj + 0x9940) + 1);
                vm.Call("08095DAC", Obj + 0x5660, Obj + 0x5660, 1);
                vm.Call("0800E3C8", Obj + 0x5660, OrigMem.Date);
                m.W16(Fam + 0x672, 0x708);
                m.W8(Obj + 0x994E, vm.Call("080D7710", Obj));
                if (m.R32(Obj + 0x9940) >= m.R32(Obj + 0x9944) || m.R8(Obj + 0x994C) == 1) { vm.Call("080D7534", Obj); return 1; }
                if (m.R8(Obj + 0x994D) == 1) { m.W32(Obj + 0x565C, m.R32(Obj + 0x5660)); return 1; }
                if (m.R8(Obj + 0x994E) == 1) { m.W32(Obj + 0x38, 3); m.W32(Obj + 0x565C, m.R32(Obj + 0x5660)); return 1; }
                if (m.R8(Obj + 0x994F) == 1) return 1;
                if (m.R32(Obj + 0x9940) % 25 == 0) return 0;
            }
        }
    }
}

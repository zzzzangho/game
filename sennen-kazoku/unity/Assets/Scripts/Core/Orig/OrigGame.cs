using System.Collections.Generic;

namespace SennenKazoku.Core.Orig
{
    /// <summary>원작 날짜 (EWRAM 0x0202C684, 3바이트 비트 묶음: 일 5비트 · 월 4비트 · 년 15비트 — ROM 0x0800E430 해독).</summary>
    public static class OrigDate
    {
        public static void Get(OrigMem m, uint a, out int year, out int month, out int day)
        {
            uint b0 = m.R8(a), b1 = m.R8(a + 1), b2 = m.R8(a + 2);
            day = (int)(b0 & 0x1F);
            month = (int)(((b0 >> 5) & 7) | ((b1 & 1) << 3));
            year = (int)(((b1 >> 1) & 0x7F) | ((b2 << 7) & 0x7F80));
        }

        public static void Set(OrigMem m, uint a, int year, int month, int day)
        {
            uint y = (uint)year, mo = (uint)month, d = (uint)day;
            m.W8(a, (d & 0x1F) | ((mo & 7) << 5));
            m.W8(a + 1, ((mo >> 3) & 1) | ((y & 0x7F) << 1));
            m.W8(a + 2, (y >> 7) & 0xFF);
        }
    }

    /// <summary>
    /// 원작 코드(변환 트리)로 하루를 진행한다. 지금은 관심사 부분만:
    /// 집에 있는 가족마다 관심사 하루 처리(0x08027E78)를 돌리고, 1(MAX)·2(MIN)이면 사건(변형 고르기 + 효과 함수),
    /// 마지막에 대기 중인 다음 관심사를 레코드에 옮긴다(0x080289C4).
    /// 원작에서 하루 처리를 부르는 시점은 인물 행동 루틴(0x08011D30)이 정한다 — 그 부분은 아직 옮기지 않아 하루 한 번 부른다.
    /// </summary>
    public sealed class OrigGame
    {
        public readonly OrigMem Mem; public readonly OrigVm Vm; public readonly OrigRules Rules;

        public sealed class DayEvent { public int Person; public bool Max; public uint Data; public int Code; }

        public OrigGame(OrigMem mem, OrigRules rules) { Mem = mem; Rules = rules; Vm = rules.CreateVm(mem); }

        public static bool Present(OrigMem m, int n) { return m.R16(OrigMem.PersonAddr(n) + 0x3C) != 0xFFFF; }

        /// <summary>하루치 관심사 처리. 돌려주는 값 = 그날 일어난 사건들.</summary>
        public List<DayEvent> TickInterests()
        {
            var evs = new List<DayEvent>();
            for (int n = 0; n < 8; n++)
            {
                if (!Present(Mem, n)) continue;
                uint p = OrigMem.PersonAddr(n);
                OrigFamily.BuildSlots(Mem, Mem.R16(p + 0x3C));
                int code = (int)Vm.Call("08027E78", p, 0);
                uint ev = EventFor(n, code);
                if (ev == 0) continue;
                int k = OrigEvents.PickVariant(Vm, Rules, ev);
                uint data = Rules.Events[ev].Data[k];
                OrigEvents.RunEffect(Vm, Rules, data);
                evs.Add(new DayEvent { Person = n, Max = code == 1, Data = data, Code = code });
            }
            OrigSelect.ApplyPending(Mem);
            return evs;
        }

        /// <summary>
        /// 하루 처리 결과 코드 → 사건 항목 (0x08014370). 1/2 지금 관심사 MAX/MIN, 6/7/8 특별 사건 표 4·5·3 의 +0x4A/+0x4C/+0x4E 번.
        /// 3(그 사람 일정 목록)·4(가족 공용 일정)는 아직 옮기지 않아 0 (사건 없음 처리).
        /// </summary>
        public uint EventFor(int n, int code)
        {
            uint p = OrigMem.PersonAddr(n);
            switch (code)
            {
                case 1: return OrigEvents.EventOf(Mem, n, true);
                case 2: return OrigEvents.EventOf(Mem, n, false);
                case 6: return Vm.Call("08119C5C", 4, Mem.R16(p + 0x4A));
                case 7: return Vm.Call("08119C5C", 5, Mem.R16(p + 0x4C));
                case 8: return Vm.Call("08119C5C", 3, Mem.R16(p + 0x4E));
            }
            return 0;
        }

        /// <summary>날짜 하루 넘기기 (그레고리력).</summary>
        public void NextDate()
        {
            OrigDate.Get(Mem, OrigMem.Date, out int y, out int mo, out int d);
            var dt = new System.DateTime(y, mo, d).AddDays(1);
            OrigDate.Set(Mem, OrigMem.Date, dt.Year, dt.Month, dt.Day);
        }
    }
}

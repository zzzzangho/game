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
    /// 원작 코드(변환 트리)로 하루를 진행한다. 원작 순서(실행 중단점으로 측정):
    /// 1) 날이 바뀌고 06:00(하루 시각 0x708) 이 되면 인물 작업(0x08011D30)마다 한 번:
    ///    생일이면 생일 사건을 사건 큐에 넣고 → 관심사 하루 처리 0x08027E78 → 결과 코드로 그날 사건 정하기(0x08014370).
    ///    그날 사건이 있으면 하루 일정 명령 8(0x080153B0)이 큐에 "인물 사건"(종류 6)을 넣는다.
    /// 2) 메인 장면이 사건 큐(0x0203BBC0)를 앞에서부터 하나씩 꺼내 사건 장면을 띄운다.
    ///    종류 6 은 이때 관계 슬롯표를 만들고 변형을 고른다(0x080187B0~). 사건 장면 = 시작 함수 → 효과 함수 → 복귀 처리.
    /// 하루 일정의 걷기·방 이동 명령은 화면 연출이라 옮기지 않는다. 하루 시각(0x0202C682)은 각 처리 때의 시각 문턱값으로 둔다. 일정 명령 8 의 시각은 옮기지 않았다:
    /// 측정한 날들에서는 그날 사건이 정해진 인물 순서대로 큐에 들어갔다(그 순서로 처리).
    /// </summary>
    public sealed class OrigGame
    {
        public readonly OrigMem Mem; public readonly OrigVm Vm; public readonly OrigRules Rules;
        public const uint Queue = 0x0203BBC0, QueueEntries = 0x0203BBD0, TimeOfDay = 0x0202C682;
        public const int QueueSize = 24;
        /// <summary>인물 작업의 그날 사건 (원작 작업 버퍼 +0xDBC, 저장되지 않음).</summary>
        public readonly uint[] TodayEvent = new uint[8];
        /// <summary>인물 작업 +0xDD0 의 0x2000 비트: 그날 새 관심사를 알린 인물 (06:00 에 꺼짐). 켜지면 그날 남은 이야기 사건은 띄우지 않는다.</summary>
        public readonly bool[] Announced = new bool[8];
        /// <summary>호출에 넘기는 결과 버퍼 자리 (원작은 호출한 쪽 지역 변수).</summary>
        const uint OutBuf = 0x0F000800;

        public sealed class DayEvent
        {
            public int Person; public uint Data; public int Code; public uint Type; public bool Max;
            public uint PersonId;                         // 사건 인물 번호(레코드 +0x3C)
            public uint[] Slots = new uint[28];           // 장면 시작 때 슬롯표 (대사 이름 자리)
            public uint Date;                             // 사건 날 (0x0202C684 3바이트)
            // 장면 앞뒤의 신님 쪽 값 (결과 스크립트가 바꾼다): 감사의 마음 0x0202C670, 신님 랭크 0x0202C66E, 화살·아이템 보유 0x0202C640~0x0202C65F
            public uint Points0, Points1, Rank0, Rank1;
            public byte[] Inv0 = new byte[32], Inv1 = new byte[32];
            /// <summary>효과 함수 결과 3단어 — 첫 단어가 장면 끝 종류(2 = 가문이 끊김, OrigEvents.RunScene 참고).</summary>
            public uint[] Result = new uint[3];
            /// <summary>결과 스크립트가 글 상자에 낸 글 바이트 (OrigText.Decode 로 푼다 — 원작 결과 문구).</summary>
            public byte[] Shown = new byte[0];
        }

        public OrigGame(OrigMem mem, OrigRules rules) { Mem = mem; Rules = rules; Vm = rules.CreateVm(mem); }

        public static bool Present(OrigMem m, int n) { return m.R16(OrigMem.PersonAddr(n) + 0x3C) != 0xFFFF; }

        /// <summary>
        /// 하루 진행. 날짜는 이미 넘긴 상태(NextDate)에서 부른다. 메인 장면 0x0801862C 의 시각별 처리 순서:
        /// 날 바뀜(1일이면 월초 사건, 1000년째면 1000년 사건) → 03:00 아크마(0x0802A640) → 06:00 인물 블록 → 큐 →
        /// 20:00 가족 일정(0x08027BE4) → 큐 → 22:00 그 달 마지막 날이면 월말 사건 → 큐.
        /// 돌려주는 값 = 그날 띄운 사건들.
        /// </summary>
        public List<DayEvent> TickDay()
        {
            var evs = new List<DayEvent>();
            var codes = new int[8];
            OrigDate.Get(Mem, OrigMem.Date, out int y, out int mo, out int d);
            uint head = Mem.R16(0x0202C67C);
            bool resumed = SceneEndedDay; SceneEndedDay = false;
            // 사건 장면이 날을 끝냈으면(아래 RunQueue) 원작은 날짜를 이미 다음 날로, 시각을 06:00 으로 적고 메인 장면을 다시 시작한다:
            // 메인 장면의 자정 넘김(날 바뀜 사건)과 03:00 은 지나지 않고 06:00 인물 블록부터 간다.
            if (!resumed)
            {
                // 날 바뀜 (0x080194A2~): 시작 날짜부터 1000년째면 특별 사건 표 2 의 3 번(종류 4), 1일이면 표 2 의 0 번(종류 1). 대상 = 가장(0x0202C67C)
                if (Years() == 1000) PushSpecial(2, 3, 4, head);
                if (d == 1) PushSpecial(2, 0, 1, head);
                // 03:00 이후 (0x0801971C~): 레코드 +0x61 이 0 아닌 첫 인물이 4 비트면 아크마가 붙는다: +0x61 = 경과 년수 단계(0x0802A7DC)+8
                Mem.W16(TimeOfDay, 900);
                int dv = (int)Vm.Call("0802A640");
                if (dv >= 0 && dv < 8 && (Mem.R8(OrigMem.PersonAddr(dv) + 0x61) & 4) != 0)
                    Mem.W8(OrigMem.PersonAddr(dv) + 0x61, Vm.Call("0802A7DC", Years()) + 8);
            }
            // 06:00 인물 블록
            Mem.W16(TimeOfDay, 0x708);
            for (int n = 0; n < 8; n++) if (Present(Mem, n)) codes[n] = Morning(n);
            evs.AddRange(RunQueue(codes));
            if (SceneEndedDay) return evs;
            // 20:00 이후 (0x08019818~): 날이 된 가족 일정을 하나씩 큐에 (종류 3, 그 인물 슬롯표 → 변형)
            Mem.W16(TimeOfDay, 6000);
            uint doy = Vm.Call("08027D0C", DateArg());
            for (int guard = 0; guard < QueueSize; guard++)
            {
                for (uint i = 0; i < 12; i += 4) Mem.W32(OutBuf + i, 0);
                if (Vm.Call("08027BE4", doy, OutBuf) == 0) break;
                uint who = Mem.R32(OutBuf) & 0xFFFF, ev = Mem.R32(OutBuf + 8);
                Vm.Call("slots", who, 0);
                Push(Rules.Events[ev].Data[OrigEvents.PickVariant(Vm, Rules, ev)], 3, who);
            }
            evs.AddRange(RunQueue(codes));
            if (SceneEndedDay) return evs;
            // 22:00 이후 (0x08019600~): 그 달 마지막 날(0x08095CD0)이면 특별 사건 표 2 의 1 번 (종류 2, 가장)
            Mem.W16(TimeOfDay, 6600);
            uint last = DateArg(); Vm.Call("08095CD0", last);
            if (d == (int)Mem.R8(last + 3)) PushSpecial(2, 1, 2, Mem.R16(0x0202C67C));
            evs.AddRange(RunQueue(codes));
            return evs;
        }

        /// <summary>시작 날짜부터 지난 해 수 (0x0802B2A4).</summary>
        uint Years() { return Vm.Call("0802B2A4", DateArg()); }

        /// <summary>원작 날짜 구조체 {u16 년, u8 월, u8 일} 를 OutBuf+0x20 에 만든다 (0x0800E430 와 같은 꼴).</summary>
        uint DateArg()
        {
            OrigDate.Get(Mem, OrigMem.Date, out int y, out int mo, out int d);
            Mem.W16(OutBuf + 0x20, (uint)y); Mem.W8(OutBuf + 0x22, (uint)mo); Mem.W8(OutBuf + 0x23, (uint)d);
            return OutBuf + 0x20;
        }

        /// <summary>특별 사건 표 a 의 b 번 → 대상 인물 중심 슬롯표 → 변형 → 큐.</summary>
        void PushSpecial(uint a, uint b, uint type, uint who)
        {
            uint ev = Vm.Call("08119B8C", Table(a, b));
            Vm.Call("slots", who, 0);
            Push(Rules.Events[ev].Data[OrigEvents.PickVariant(Vm, Rules, ev)], type, who);
        }

        /// <summary>인물 n 의 06:00 블록 (0x08011D30 의 하루 한 번 부분 중 규칙 상태를 바꾸는 것). 돌려주는 값 = 하루 처리 결과 코드.</summary>
        public int Morning(int n)
        {
            uint p = OrigMem.PersonAddr(n), id = Mem.R16(p + 0x3C);
            TodayEvent[n] = 0; Announced[n] = false;
            if (IsBirthday(p))
            {
                // 특별 사건 표 2 의 2 번 (0x08119B8C {2,2}) → 그 인물 중심 슬롯표 → 변형 고르기 → 큐 {결과, 종류 0, 인물}
                uint ev = Vm.Call("08119B8C", Table(2, 2));
                Vm.Call("slots", id, 0);
                Push(Rules.Events[ev].Data[OrigEvents.PickVariant(Vm, Rules, ev)], 0, id);
            }
            int code = (int)Vm.Call("08027E78", p, 0);
            if (code != 0) Dispatch(n, code);
            if (TodayEvent[n] != 0) Push(0, 6, id);   // 일정 명령 8 (0x080153B0)
            // 블록 끝(0x08012178~): 아크마가 붙을지 (0x0802A6C4: 나이 단계·관심사·레코드 +0x4B 에 따라 1/4 또는 1/16) → +0x61 = 4
            if (Vm.Call("0802A6C4", id) != 0) Mem.W8(p + 0x61, 4);
            return code;
        }

        uint Table(uint a, uint b) { Mem.W16(OutBuf + 0x40, a); Mem.W16(OutBuf + 0x42, b); return OutBuf + 0x40; }

        /// <summary>생일 확인: 오늘 월·일 = 생일 월·일, 또는 생일 2/29 이고 올해가 윤년이 아니면 3/1 (0x08096010 윤년).</summary>
        bool IsBirthday(uint p)
        {
            OrigDate.Get(Mem, OrigMem.Date, out int y, out int mo, out int d);
            OrigDate.Get(Mem, p + 0x2E, out _, out int bm, out int bd);
            if (bd == d && bm == mo) return true;
            return bd == 29 && bm == 2 && Vm.Call("08096010", (uint)y) == 0 && d == 1 && mo == 3;
        }

        /// <summary>0x08014370: 하루 처리 결과 코드 → 그날 사건(작업 +0xDBC).</summary>
        void Dispatch(int n, int code)
        {
            uint p = OrigMem.PersonAddr(n);
            switch (code)
            {
                case 1: TodayEvent[n] = OrigEvents.EventOf(Mem, n, true); break;
                case 2: TodayEvent[n] = OrigEvents.EventOf(Mem, n, false); break;
                case 3:   // 그 사람의 일정 목록 0x08026D74(레코드, 결과, 0): 결과 +8 사건, +4 → 레코드 +0x5F
                    for (uint i = 0; i < 12; i += 4) Mem.W32(OutBuf + i, 0);
                    Vm.Call("08026D74", p, OutBuf, 0);
                    TodayEvent[n] = Mem.R32(OutBuf + 8); Mem.W8(p + 0x5F, Mem.R32(OutBuf + 4));
                    break;
                case 4:   // 가족 공용 일정 0x08027090(0, 결과, 0x08014398): 결과 +0 대상 작업 번호(-1 이면 0x0202C67C 인물)
                    {
                        for (uint i = 0; i < 12; i += 4) Mem.W32(OutBuf + i, 0);
                        Vm.Call("08027090", 0, OutBuf, 0x08014398);
                        TodayEvent[n] = Mem.R32(OutBuf + 8);
                        uint who = Mem.R32(OutBuf);
                        bool mine = who != 0xFFFFFFFF ? who == (uint)n : Mem.R16(p + 0x3C) == Mem.R16(0x0202C67C);
                        if (who != 0xFFFFFFFF && !mine) { TodayEvent[n] = 0; break; }
                        if (!mine) TodayEvent[n] = 0;
                        Mem.W8(p + 0x5F, Mem.R32(OutBuf + 4));
                        break;
                    }
                case 6: TodayEvent[n] = Vm.Call("08119C5C", 4, Mem.R16(p + 0x4A)); break;
                case 7: TodayEvent[n] = Vm.Call("08119C5C", 5, Mem.R16(p + 0x4C)); break;
                case 8: TodayEvent[n] = Vm.Call("08119C5C", 3, Mem.R16(p + 0x4E)); break;
            }
        }

        /// <summary>0x080233AC: 사건 큐에 넣기 (24칸 원형, 쓰는 번호 0x0203BBC8).</summary>
        public void Push(uint data, uint type, uint id)
        {
            uint w = Mem.R16(Queue + 8), e = QueueEntries + 8 * w;
            Mem.W32(e, data); Mem.W16(e + 4, type); Mem.W16(e + 6, id);
            w++; if (w == QueueSize) w = 0;
            Mem.W16(Queue + 8, w);
        }

        /// <summary>메인 장면의 큐 처리 (0x08018642~): 읽는 번호(+0xA) 가 쓰는 번호(+8) 를 따라갈 때까지 사건 장면을 띄운다.</summary>
        List<DayEvent> RunQueue(int[] codes)
        {
            var evs = new List<DayEvent>();
            for (int guard = 0; guard < QueueSize && Mem.R16(Queue + 8) != Mem.R16(Queue + 0xA); guard++)
            {
                uint r = Mem.R16(Queue + 0xA), e = QueueEntries + 8 * r;
                uint type = Mem.R16(e + 4), id = Mem.R16(e + 6);
                int n = (int)Vm.Call("0800E524", id);
                // 종류 1~4 (월초·월말·가족 일정·1000년) 는 넣을 때 변형을 이미 골랐다. 원작 실행에서 종류 1·2 도 같은 사건 장면 경로로 처리됨을 확인.
                if (type == 6)
                {
                    Mem.W16(e + 4, 0);
                    uint ev = n >= 0 && n < 8 ? TodayEvent[n] : 0;
                    if (ev == 0) throw new OrigUnmodeled("큐의 인물 사건에 그날 사건이 없음");
                    Vm.Call("slots", id, ev);
                    Mem.W32(e, Rules.Events[ev].Data[OrigEvents.PickVariant(Vm, Rules, ev)]);
                }
                uint data = Mem.R32(e);
                r++; if (r == QueueSize) r = 0;
                Mem.W16(Queue + 0xA, r);
                // 0x080187F2~: 결과 기록 +0 종류가 5 초과·0xB 아니고 그 인물이 그날 새 관심사를 알렸으면 띄우지 않고 넘긴다
                uint kind = Mem.R8(data);
                if (kind > 5 && kind != 0xB && n >= 0 && n < 8 && Announced[n]) continue;
                // 메인 장면을 떠날 때(0x080211F4, 0x080213EC) 큐 머리에 가족 수(0x08015BC4)와 가족 표지를 적는다.
                // 장면 복귀 0x080111B8 은 둘이 지금 값과 다르면(사건으로 가족이 바뀜) 그날을 끝내고 다음 날 06:00 으로 넘긴다.
                Mem.W16(Queue + 0xE, FamilyKey()); Mem.W16(Queue + 6, Vm.Call("08015BC4"));
                uint date0 = Mem.R32(OrigMem.Date) & 0xFFFFFF;
                var de = new DayEvent { Person = n, Data = data, Type = type, Code = n >= 0 && n < 8 ? codes[n] : 0, Max = n >= 0 && n < 8 && codes[n] == 1,
                    PersonId = id, Date = date0 };
                de.Points0 = Mem.R32(0x0202C670); de.Rank0 = Mem.R16(0x0202C66E);
                for (uint i = 0; i < 32; i++) de.Inv0[i] = (byte)Mem.R8(0x0202C640 + i);
                var shown = new List<byte>();
                OrigEvents.RunScene(Vm, Rules, data, id, de.Slots, de.Result, shown);
                de.Shown = shown.ToArray();
                if (de.Result[0] >= 1 && de.Result[0] <= 4) EndCodes.Add(de.Result[0] + "@" + data.ToString("X8"));
                de.Points1 = Mem.R32(0x0202C670); de.Rank1 = Mem.R16(0x0202C66E);
                for (uint i = 0; i < 32; i++) de.Inv1[i] = (byte)Mem.R8(0x0202C640 + i);
                evs.Add(de);
                AnnouncePending();
                if ((Mem.R32(OrigMem.Date) & 0xFFFFFF) != date0) { SceneEndedDay = true; break; }
            }
            return evs;
        }

        /// <summary>
        /// 사건 장면 뒤 큐 작업(0x0801E724, 0x0801ECFE~): 대기 중인 다음 관심사(0x0203BD20 인물 / 0x0203BD40 관심사)를 하나씩
        /// 찾아(0x08028A10) 그 인물 레코드 +0x80 에 넣고(0x08028AAC) "새 관심사" 알림을 띄운다. 알린 인물은 작업 +0xDD0 에 0x2000.
        /// </summary>
        void AnnouncePending()
        {
            for (int guard = 0; guard < 8; guard++)
            {
                uint k = Vm.Call("08028A10");
                if (k == 0xFFFFFFFF) return;
                int n = (int)Vm.Call("0800E524", Mem.R16(0x0203BD20 + 2 * k));
                if (n >= 0 && n < 8) Announced[n] = true;
                Vm.Call("08028AAC", (uint)n, k);
            }
        }

        /// <summary>가족 표지 (0x0801165A·0x080212DA): (0x0202C6AF & 0xF) + (0x0202C692 << 8).</summary>
        uint FamilyKey() { return (Mem.R8(0x0202C6AF) & 0xF) + (Mem.R8(0x0202C692) << 8); }

        /// <summary>마지막 사건 장면 복귀(0x080111B8)가 그날을 끝내고 날짜를 다음 날 06:00 으로 넘겼는지. 다음 NextDate 는 날짜를 더하지 않는다.</summary>
        public bool SceneEndedDay { get; private set; }
        /// <summary>시험용: 보통 복귀가 아닌 장면 끝 종류가 나온 사건 ("종류@결과 기록").</summary>
        public readonly List<string> EndCodes = new List<string>();
        /// <summary>저장에서 이어 할 때: 마지막 장면이 날을 끝낸 상태였으면 다음 날을 06:00 부터.</summary>
        public void ResumeMorning() { SceneEndedDay = true; }

        /// <summary>날짜 하루 넘기기 (그레고리력). 원작은 메인 장면에서 0x08095DAC 로 더한다. 사건 장면이 이미 날을 넘겼으면 그대로 둔다.</summary>
        public void NextDate()
        {
            if (SceneEndedDay) return;
            OrigDate.Get(Mem, OrigMem.Date, out int y, out int mo, out int d);
            var dt = new System.DateTime(y, mo, d).AddDays(1);
            OrigDate.Set(Mem, OrigMem.Date, dt.Year, dt.Month, dt.Day);
        }
    }
}

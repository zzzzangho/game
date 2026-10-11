using System;
using System.Collections.Generic;

namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 사건 메모 장면 흐름 (이어 하기 때 원작 장면 0x080C9488 의 큐피트 글 · 진행 · 보고 · 물음). 규칙은 원작 함수, 화면 순서는 원작 장면 상태를 따른다:
    /// 1) 인사(0x080CCA3C): 통신 결혼의 화살(0x080D8190)이면 62, 지난 햇수(0x080D448C) &gt; 999 면 63, 햇수가 있으면 60(햇수), 아니면 61(일 수).
    /// 2) 진행: 하루 묶음(OrigOffline.Step, 25일씩)을 멈출 때까지. 화면에는 날짜와 마지막 메모(인물·나이·제목) — 원작 "사건 메모 확인 중" 말풍선.
    ///    일 수가 0 이면 원작은 진행 상태(0x5D)로 바로 간다 — 앱은 인사 뒤 끝낸다.
    /// 3) 보고: 첫 글(0x080CCF6C) = 화살이면 100/101, 보고 차례(0x02029BA0) 1 이면 64, 아니면 세대교체 멈춤이면 65 아니면 66.
    ///    그다음 글은 원작 보고 함수 0x080D5CE0(변환 트리)이 고른다 — 실기 기록과 글·인물 순서가 같음(검사).
    /// 4) 물음(0x080CFA74): 가계 끊김(+0x994D) 94 · 천년가족(+0x994E) 97 · 세대교체(+0x994C) 81 · 화살(+0x994F) 102 · 끝 82.
    ///    예(0x080D015C): 95 · 98 · 83 · 103 · 84 → 세대교체면 천계 보고 뒤 남은 날을 이어서(장면 방식 1). 아니오: 99/85 → 메모 보기 → 다시 물음.
    /// </summary>
    public sealed class OrigMemoFlow
    {
        enum Phase { Greeting, Progress, Report, Question, Answer, MemoIntro, Memo, Heaven, Done }

        public OrigOffline Off { get; private set; }
        readonly OrigVm vm; readonly OrigMem m; readonly OrigText text;
        readonly Func<int, string> personName; readonly Func<string> player; readonly Func<int, int> ageOf;
        Phase phase; readonly List<string> pages = new List<string>(); int page;
        int answerMsg, lastReport = -1, memoPage;
        const int MemoPer = 3;
        /// <summary>메모 보기에서 고른 항목 — 세션이 그 사건 장면을 효과 없이 다시 보여 준 뒤 지운다.</summary>
        public Entry ReplayRequest;
        /// <summary>마지막 보고에서 고른 글 번호 (검사용).</summary>
        public readonly List<int> ReportIdsForTest = new List<int>();
        public bool Done { get { return phase == Phase.Done; } }
        /// <summary>물음에 "예" 하고 세대교체였으면 참 — 세션이 천계 보고를 띄운 뒤 Continue 를 부른다.</summary>
        public bool WantsHeaven { get; private set; }
        public bool LineageEnded { get; private set; }

        const uint Rep = 0x0F03C100;   // 보고 객체 (원작 장면 하위 객체 — +0 장면, +0x48 지금 글, +0x4C 중요 메모 차례, +0x4D 날짜, +0x51 표시)
        const uint Fam = OrigMem.Fam, Obj = OrigOffline.Obj;
        const uint MsgTable = 0x085D5CB0;

        public OrigMemoFlow(OrigVm vm, OrigText text, Func<int, string> personName, Func<string> player, Func<int, int> ageOf, uint days)
        {
            this.vm = vm; m = vm.Mem; this.text = text; this.personName = personName; this.player = player; this.ageOf = ageOf;
            Off = OrigOffline.Begin(vm, days);
            Greeting();
        }

        // ---------------- 화면 ----------------
        public EventView View()
        {
            if (phase == Phase.Done) return null;
            var v = new EventView { EventId = phase == Phase.Progress ? "memo-progress" : "memo", Title = "사건 메모", Speaker = phase == Phase.Progress ? "" : "큐피트",
                Phase = phase == Phase.Question ? "choices" : "pages", Origin = "original", Certainty = "rom", TextSource = "original-translation", PersonId = -1 };
            if (phase == Phase.Progress) { v.Text = ProgressText(); v.TextSource = "app-summary"; return v; }
            if (phase == Phase.Memo)
            {
                var es = Entries(); int pagesN = Math.Max(1, (es.Count + MemoPer - 1) / MemoPer);
                memoPage = Math.Min(memoPage, pagesN - 1);
                var sb = new System.Text.StringBuilder("사건 메모 " + (memoPage + 1) + "/" + pagesN);
                for (int i = memoPage * MemoPer; i < Math.Min(es.Count, (memoPage + 1) * MemoPer); i++)
                {
                    var e = es[i];
                    sb.Append('\n').Append(i - memoPage * MemoPer + 1).Append(". ");
                    if (e.Date >= 0) sb.Append(GameDate.Year(e.Date)).Append('.').Append(GameDate.Month(e.Date)).Append('.').Append(GameDate.Day(e.Date)).Append(' ');
                    sb.Append(personName(e.PersonId)).Append(" 『").Append(e.Title.Length > 0 ? e.Title : "?").Append("』");
                    v.Choices.Add(new ChoiceView { Id = "view:" + i, Text = (i - memoPage * MemoPer + 1) + " 보기" });
                }
                if (es.Count == 0) sb.Append("\n(메모 없음)");
                if (pagesN > 1) { v.Choices.Add(new ChoiceView { Id = "prev", Text = "◀ 앞" }); v.Choices.Add(new ChoiceView { Id = "next", Text = "다음 ▶" }); }
                v.Choices.Add(new ChoiceView { Id = "close", Text = "닫기 (B)" });
                v.Text = sb.ToString(); v.Phase = "choices"; v.TextSource = "original-data"; v.Speaker = "";
                return v;
            }
            v.Text = pages.Count > 0 ? pages[Math.Min(page, pages.Count - 1)] : "";
            if (phase == Phase.Memo) v.TextSource = "original-data";
            if (phase == Phase.Question && page >= pages.Count - 1)
            {
                v.Choices.Add(new ChoiceView { Id = "yes", Text = "응" });
                v.Choices.Add(new ChoiceView { Id = "no", Text = "아니" });
            }
            else if (phase == Phase.Question) v.Phase = "pages";
            return v;
        }

        /// <summary>다음 장. 흐름이 다 끝나면 참.</summary>
        public bool Advance()
        {
            switch (phase)
            {
                case Phase.Progress:
                    if (Off.Step() == 1) StartReport();
                    return false;
                case Phase.Question:
                    if (page < pages.Count - 1) page++;
                    return false;
            }
            if (page < pages.Count - 1) { page++; return false; }
            switch (phase)
            {
                case Phase.Greeting:
                    if (Off.Days == 0) { StartReport(); return false; }   // 원작: 일 수 0 이면 진행 없이 바로 보고(상태 0x5D) — 실기 61 → 64 68 71 74 86 89 75
                    phase = Phase.Progress; return false;
                case Phase.Report:
                    if (lastReport == 75) { phase = Phase.Done; return true; }   // 75 "함께 지켜보자！ 출발！！" 로 끝나면 물음 없이 집으로 (실기)
                    Question(); return false;
                case Phase.MemoIntro: phase = Phase.Memo; memoPage = 0; return false;
                case Phase.Answer:
                    if (answerMsg == 83) { WantsHeaven = true; phase = Phase.Heaven; return false; }   // 세션이 천계 보고를 띄운 뒤 Continue / Finish
                    if (answerMsg == 95) LineageEnded = true;
                    phase = Phase.Done; return true;
            }
            return false;
        }

        public void Choose(string id)
        {
            if (phase == Phase.Memo)
            {   // 메모 보기 (원작은 목록에서 고르면 그 사건 장면을 다시 보여 주고 B 로 닫는다)
                var es = Entries(); int pagesN = Math.Max(1, (es.Count + MemoPer - 1) / MemoPer);
                if (id == "prev") memoPage = (memoPage + pagesN - 1) % pagesN;
                else if (id == "next") memoPage = (memoPage + 1) % pagesN;
                else if (id == "close") Question();
                else if (id.StartsWith("view:") && int.TryParse(id.Substring(5), out int k) && k >= 0 && k < es.Count) ReplayRequest = es[k];
                return;
            }
            if (phase != Phase.Question) return;
            if (id == "yes")
            {
                answerMsg = m.R8(Obj + 0x994D) == 1 ? 95 : m.R8(Obj + 0x994E) == 1 ? 98 : m.R8(Obj + 0x994C) == 1 ? 83 : m.R8(Obj + 0x994F) == 1 ? 103 : 84;
                SetPages(Phase.Answer, Msg(answerMsg, Args.None));
            }
            else
            {
                int no = m.R8(Obj + 0x994D) == 1 || m.R8(Obj + 0x994E) == 1 ? 99 : 85;
                SetPages(Phase.MemoIntro, Msg(no, Args.None));
            }
        }

        /// <summary>흐름을 끝낸다 (남은 날 없이 천계로 가는 경우).</summary>
        public void Finish() { WantsHeaven = false; phase = Phase.Done; }

        /// <summary>천계 보고 뒤 남은 날 이어 하기 (장면 방식 1). 남은 날이 없으면 끝.</summary>
        public void Continue()
        {
            WantsHeaven = false;
            Off = OrigOffline.BeginNext(vm);
            if (Off.Days == 0) { phase = Phase.Done; return; }
            phase = Phase.Progress; pages.Clear(); page = 0;
        }

        void SetPages(Phase p, List<string> l) { phase = p; pages.Clear(); pages.AddRange(l); page = 0; }

        // ---------------- 단계 ----------------
        void Greeting()
        {
            uint t = Scratch;
            vm.Call("080D448C", t, t + 2, Obj + 0x5658, Obj + 0x565C);
            uint years = m.R16(t), days = m.R16(t + 2);
            int id; uint num;
            if ((vm.Call("080D8190", Obj) & 0xFF) != 0) { id = 62; num = 0; }
            else if (years > 999) { id = 63; num = years; }
            else if (years != 0) { id = 60; num = years; }
            else { id = 61; num = days; }
            SetPages(Phase.Greeting, Msg(id, new Args { Numbers = new[] { 0u, 0u, num } }));
        }
        const uint Scratch = 0x0F03C0C0;

        /// <summary>보고 글 목록: 첫 글은 장면이 직접(0x080CD4F8~), 그다음은 원작 보고 함수 0x080D5CE0 이 하나씩 고른다.</summary>
        void StartReport()
        {
            var l = new List<string>(); lastReport = -1;
            ReportIdsForTest.Clear();
            foreach (var a in ReportMessages()) { l.AddRange(a.Pages); lastReport = a.Id; ReportIdsForTest.Add(a.Id); }
            SetPages(Phase.Report, l);
        }

        public sealed class Args
        {
            public static readonly Args None = new Args();
            public uint Person = 0xFFFF, NumBuf, DateArg, TitleStruct, Relation, Record;
            public uint[] Numbers;
            public int Id;
            /// <summary>글을 고른 그 순간의 날짜·숫자·슬롯표로 푼 장 (보고 객체 칸은 다음 글에서 바뀐다).</summary>
            public List<string> Pages;
        }

        /// <summary>보고 글(원작 함수가 고른 차례·인자 그대로). 시험이 실기 기록과 비교한다.</summary>
        public List<Args> ReportMessages()
        {
            for (uint a = Rep; a < Rep + 0xA0; a++) m.W8(a, 0);
            m.W32(Rep, Obj); m.W32(Rep + 0xC, 3); m.W32(Rep + 0x1C, 0xFF);
            int first;
            if ((vm.Call("080D8190", Obj) & 0xFF) != 0) first = m.R8(Obj + 0x994F) != 0 ? 100 : 101;
            else if (m.R8(0x02029BA0) == 1) first = 64;
            else first = m.R8(Obj + 0x994C) == 1 ? 65 : 66;
            m.W32(Rep + 0x48, (uint)first);
            vm.Call("0800E3C8", Obj + 0x5658, Rep + 0x4D);
            uint gen = m.R8(Fam + 0x6A3);
            uint head = m.R16(Fam + 0x2538 + 28 * (gen - 2));
            vm.Call("slots", head, 0);
            var a0 = new Args { Id = first, Person = head, NumBuf = Rep + 0x1C, DateArg = Rep + 0x4D }; a0.Pages = Msg(first, a0);
            var list = new List<Args> { a0 };
            for (int k = 0; k < 64; k++)
            {
                vm.Messages = new List<uint[]>();
                uint idx = m.R8(Rep + 0x4C);
                try { vm.Call("080D5CE0", Rep); }
                finally { }
                var got = vm.Messages; vm.Messages = null;
                if (got.Count == 0) break;
                foreach (var g in got)
                {
                    int id = MsgId(g[1]);
                    var a = new Args { Id = id, Person = g[0], NumBuf = g[2], DateArg = g[4], TitleStruct = g[5], Relation = g[6] };
                    if (id >= 77 && id <= 79)
                    {
                        uint e = m.R32(Obj + 0x5684 + 4 * idx);
                        if (e != 0) a.Record = m.R32(e + 0x10);
                    }
                    a.Pages = Msg(id, a);
                    list.Add(a);
                }
            }
            return list;
        }

        void Question()
        {
            int id = m.R8(Obj + 0x994D) == 1 ? 94 : m.R8(Obj + 0x994E) == 1 ? 97 : m.R8(Obj + 0x994C) == 1 ? 81 : m.R8(Obj + 0x994F) == 1 ? 102 : 82;
            SetPages(Phase.Question, Msg(id, Args.None));
        }

        // ---------------- 글 ----------------
        int MsgId(uint ptr)
        {
            // 같은 글을 가리키는 번호가 여럿이면(77·78) 처음 것
            for (int i = 60; i < 108; i++) if (m.R32(MsgTable + 4 * (uint)i) == ptr) return i;
            return -1;
        }

        List<string> Msg(int id, Args a)
        {
            byte[] b;
            if (text.Heaven == null || !text.Heaven.Msgs.TryGetValue(id, out b)) return new List<string> { "(사건 메모 글 " + id + " — 로컬 글 팩에 없음)" };
            return text.Decode(b, (code, fmt) => Name(code, fmt, a), null, (x, y) => Number(x, y, a));
        }

        string Name(int code, int fmt, Args a)
        {
            if (code == 0x26) return player();
            if (code == 0x27) return personName((int)OrigResultScript.MarkFamily);
            if (code == 0x2A)
            {   // 글 엔진 0x080AA1F4: 1C 오늘 · 1D 세대주(가족 +0x66C) · 1E 가족 +0x66E · FF 넘겨받은 날짜(보고 객체 +0x4D)
                if (fmt == 0x1D) return personName((int)m.R16(Fam + 0x66C));
                if (fmt == 0x1E) return personName((int)m.R16(Fam + 0x66E));
                uint at = fmt == 0x1C ? OrigMem.Date : a.DateArg;
                if (at == 0) return null;
                OrigDate.Get(m, at, out int y, out int mo, out int d);
                return y + "년" + mo + "월" + d + "일";
            }
            if (code == 0x2B)
            {
                if (a.Record != 0 && text.Records.TryGetValue(a.Record, out var r) && r.Title.Length > 0) return r.Title;
                return null;
            }
            if (code == 0x2C) return text.UiText((int)a.Relation, null);   // 관계 이름 함수(0x080F95D4·0x080FA534·0x080F9F1C)가 돌려준 화면 글 번호 (409 아버지 · 410 남편 …)
            if (code < 0x1C)
            {
                uint id = m.R16(OrigMem.Slots + 2 * (uint)code);
                return id == 0xFFFF ? null : personName((int)id);
            }
            return null;
        }

        string Number(int x, int y, Args a)
        {
            if (x == 4) return m.R8(Fam + 0x6A3).ToString();
            if (x == 2)
            {
                if (a.Numbers != null && y < a.Numbers.Length) return a.Numbers[y].ToString();
                if (a.NumBuf != 0) return m.R32(a.NumBuf + 4 + 4 * (uint)y).ToString();
            }
            return null;
        }

        // ---------------- 메모 ----------------
        public sealed class Entry { public int Date, PersonId; public uint Record; public string Title = ""; }

        /// <summary>메모 목록 (장면 +0x566C 부터 +4 다음 항목): +0xA 인물 번호, +0x10 결과 기록, +0x14 날짜(년·512 + 월·32 + 일).</summary>
        public List<Entry> Entries()
        {
            var l = new List<Entry>();
            uint p = m.R32(Obj + 0x566C); int guard = 0;
            while (p != 0 && p != Obj + 0x5668 && guard++ < 800)
            {
                if (m.R8(p + 8) != 0)
                {
                    uint dc = m.R32(p + 0x14);
                    int yy = (int)(dc >> 9), mm = (int)((dc >> 5) & 15), dd = (int)(dc & 31);
                    var e = new Entry { PersonId = (int)m.R16(p + 0xA), Record = m.R32(p + 0x10), Date = yy >= 1 && yy <= 9999 && mm >= 1 && mm <= 12 && dd >= 1 && dd <= 31 ? GameDate.Make(yy, mm, Math.Min(dd, DateTime.DaysInMonth(yy, mm))) : -1 };
                    if (text.Records.TryGetValue(e.Record, out var r)) e.Title = r.Title;
                    l.Add(e);
                }
                p = m.R32(p + 4);
            }
            return l;
        }

        string ProgressText()
        {
            OrigDate.Get(m, OrigMem.Date, out int y, out int mo, out int d);
            var sb = new System.Text.StringBuilder("사건 메모 확인 중\n" + y + "년" + mo + "월" + d + "일");
            var es = Entries();
            if (es.Count > 0)
            {
                var e = es[es.Count - 1];
                int age = ageOf(e.PersonId);
                sb.Append("\n").Append(personName(e.PersonId)).Append(age >= 0 ? " (" + age + "세)" : "").Append("\n").Append(e.Title);
            }
            return sb.ToString();
        }
    }
}

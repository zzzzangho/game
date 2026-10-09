using System;
using System.Collections.Generic;

namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 원작 코드(변환 트리)로 진행하는 세션 — 화면은 이전 GameSession 과 같은 방식(IGameSession)으로 쓴다.
    /// 원작 메모리가 기준이고, 화면용 Family 는 하루마다 원작 레코드에서 다시 만든다(투영).
    ///  - 인물: 레코드 0x0202C6C4+976n 중 번호(+0x3C)가 있는 것. 생일 +0x2E, 성별 +0x31, 능력치 +0x50~+0x56, 게이지 +0x5A,
    ///    몰입도 +0x5B, 직업 +0x58, 관심사 날짜 +0x48, 화살 표시 +0x69, 성격 +0x32, 관심사 +0x80/+0x82.
    ///  - 관계: 족보 0x0202EB9C+60×번호 의 +0x10 아버지 · +0x12 어머니 · +0x14 배우자. 가장 0x0202C67C, 시작 날짜 0x0202C688.
    /// 사건은 원작처럼 그날 진행(OrigGame.TickDay) 안에서 효과까지 끝나고, 화면에는 그 뒤 대사(로컬 글 대응표)로 보여 준다.
    /// 아직 원작대로 하지 못한 것(표시):
    ///  - 이름: 원작 이름 글자표를 해독하지 못해 앱이 붙인 이름을 쓴다(OrigNames).
    ///  - 결과 예고(Predict)·사건 중 선택지·하트(무드) 표시는 아직 없다.
    ///  - 아직 보여 주지 않은 그날 사건 목록은 저장하지 않는다(효과는 이미 원작 메모리에 들어가 있다).
    /// </summary>
    public sealed class OrigSession : IGameSession
    {
        public Family Family { get; private set; }
        public readonly OrigGame Game;
        public readonly OrigRules Rules;
        public readonly OrigText Text;
        public readonly List<EffectChange> LastChanges = new List<EffectChange>();

        readonly Queue<OrigGame.DayEvent> pending = new Queue<OrigGame.DayEvent>();
        OrigGame.DayEvent cur; List<string> pages; int page;

        public bool Paused { get { return cur != null; } }

        /// <summary>표시 이름이 없는 새 인물에게 붙이는 이름 (앱 표시용, 원작 이름 아님).</summary>
        static readonly string[] MaleNames = { "민준", "서준", "도윤", "하준", "지호", "준우", "현우", "건우", "우진", "선우", "유준", "정우", "승현", "시우", "지훈", "태윤" };
        static readonly string[] FemaleNames = { "서연", "지우", "하윤", "서윤", "민서", "하은", "지아", "수아", "지유", "채원", "윤서", "다은", "예린", "소율", "가은", "나연" };

        OrigSession(OrigRules rules, OrigText text, Family f, OrigMem m)
        {
            Rules = rules; Text = text ?? new OrigText(); Family = f;
            Game = new OrigGame(m, rules);
            Project();
        }

        /// <summary>원작 "신이 추천하는 가족"으로 새로 시작. seed = 원작 난수(0x02000000) 시작값(원작이 정하는 방법은 미해독 — 앱이 정한다).</summary>
        public static OrigSession NewRecommended(OrigRules rules, OrigText text, string surname, uint seed, int year = 2005, int month = 1, int day = 1)
        {
            var m = new OrigMem(); var vm = rules.CreateVm(m);
            OrigNewGame.BlankCartridge(vm);
            OrigNewGame.TitleNewGame(vm);
            m.W32(OrigMem.Seed, seed);
            OrigNewGame.Recommended(vm, year, month, day);
            var f = new Family { Name = surname ?? "" };
            var s = new OrigSession(rules, text, f, m);
            s.PrepareSave();
            return s;
        }

        /// <summary>저장한 원작 가족 이어 하기 (Family.OrigState).</summary>
        public static OrigSession Load(OrigRules rules, OrigText text, Family f)
        {
            if (f == null || !f.IsOriginal) throw new ArgumentException("원작 가족 저장이 아님");
            var b = Convert.FromBase64String(f.OrigState);
            var m = new OrigMem();
            int n = (int)(OrigMem.SaveEnd - OrigMem.SaveStart);
            var blk = new byte[n]; Array.Copy(b, 0, blk, 0, Math.Min(n, b.Length));
            m.LoadSaveBlock(blk);
            if (b.Length >= n + 4) m.W32(OrigMem.Seed, BitConverter.ToUInt32(b, n));
            var s = new OrigSession(rules, text, f, m);
            if (b.Length >= n + 5 && b[n + 4] != 0) s.Game.ResumeMorning();
            return s;
        }

        /// <summary>원작 메모리(세이브 영역 + 난수 seed + 장면이 날을 끝냈는지)를 Family.OrigState 에 적는다.</summary>
        public void PrepareSave()
        {
            var blk = Game.Mem.SaveBlock();
            var b = new byte[blk.Length + 5];
            Array.Copy(blk, b, blk.Length);
            Array.Copy(BitConverter.GetBytes(Game.Mem.R32(OrigMem.Seed)), 0, b, blk.Length, 4);
            b[blk.Length + 4] = (byte)(Game.SceneEndedDay ? 1 : 0);
            Family.OrigState = Convert.ToBase64String(b);
        }

        public void ReplaceCatalog(ContentCatalog c) { }

        // ---------- 하루 진행 ----------
        public bool StepDay()
        {
            if (cur != null) return false;
            if (pending.Count > 0) { StartNext(); return true; }
            SyncArrows();
            Game.NextDate();
            foreach (var e in Game.TickDay()) pending.Enqueue(e);
            Project();
            if (pending.Count > 0) { StartNext(); return true; }
            return false;
        }

        /// <summary>화면에서 쏜 화살(Interventions — 원작 0x080244A0 과 같은 비트 규칙)을 원작 레코드 +0x69 에 옮긴다.</summary>
        void SyncArrows()
        {
            for (int n = 0; n < 8; n++)
            {
                if (!OrigGame.Present(Game.Mem, n)) continue;
                uint p = OrigMem.PersonAddr(n);
                var q = Family.Get((int)Game.Mem.R16(p + 0x3C));
                if (q != null && Game.Mem.R8(p + 0x69) != (uint)q.ArrowFlags) Game.Mem.W8(p + 0x69, (uint)q.ArrowFlags & 0xFF);
            }
        }

        void StartNext()
        {
            cur = pending.Dequeue(); page = 0;
            OrigText.Rec rec;
            if (Text.Records.TryGetValue(cur.Data, out rec) && !string.IsNullOrEmpty(rec.Script))
                pages = OrigText.Pages(rec.Script, SlotName);
            else pages = new List<string>();
            if (pages.Count == 0) pages.Add("(원작 사건 " + cur.Data.ToString("X8") + " — 대사 자료 없음)");
        }

        string SlotName(int code)
        {
            if (code == 0x27) return Family.Name;
            if (code < 0 || code >= 28 || cur == null) return null;
            uint id = cur.Slots[code];
            return id == 0xFFFF ? null : NameOf((int)id, -1);
        }

        string NameOf(int id, int gender)
        {
            string s;
            if (Family.OrigNames.TryGetValue(id, out s) && !string.IsNullOrEmpty(s)) return s;
            if (gender < 0) { var p = Family.Get(id); gender = p != null ? p.Gender : 0; }
            var list = gender == 1 ? FemaleNames : MaleNames;
            s = list[id % list.Length];
            Family.OrigNames[id] = s;
            return s;
        }

        public EventView View()
        {
            if (cur == null) return null;
            OrigText.Rec rec; Text.Records.TryGetValue(cur.Data, out rec);
            return new EventView
            {
                EventId = rec != null ? rec.Id : "0x" + cur.Data.ToString("X8"),
                Title = rec != null && rec.Title.Length > 0 ? rec.Title : "원작 사건",
                Speaker = "", Text = pages[Math.Min(page, pages.Count - 1)], Phase = "pages",
                Origin = "original", Certainty = "rom", TextSource = rec != null ? "reference-translation" : "none",
                PersonId = (int)cur.PersonId
            };
        }

        public bool Advance()
        {
            if (cur == null) return false;
            if (page < pages.Count - 1) { page++; return false; }
            OrigText.Rec rec; Text.Records.TryGetValue(cur.Data, out rec);
            Family.History.Add(new EventRecord { Day = Family.Today, EventId = rec != null ? rec.Id : "0x" + cur.Data.ToString("X8"),
                Title = rec != null ? rec.Title : "원작 사건", PersonId = (int)cur.PersonId });
            cur = null;
            return true;
        }

        public bool Choose(string choiceId) { return false; }
        public Prediction Predict(int personId, bool max) { return null; }

        // ---------- 원작 레코드 → 화면용 가족 ----------
        public void Project()
        {
            var m = Game.Mem; var f = Family;
            var old = new Dictionary<int, Person>(); foreach (var p in f.Members) old[p.Id] = p;
            f.Members.Clear();
            for (int n = 0; n < 8; n++)
            {
                if (!OrigGame.Present(m, n)) continue;
                uint a = OrigMem.PersonAddr(n);
                int id = (int)m.R16(a + 0x3C);
                Person p; if (!old.TryGetValue(id, out p)) p = new Person { Id = id };
                p.Gender = (int)m.R8(a + 0x31) == 0 ? 0 : 1;
                OrigDate.Get(m, a + 0x2E, out int by, out int bm, out int bd);
                p.BirthDay = SafeDay(by, bm, bd);
                for (int k = 0; k < 4; k++) p.Stats[k] = (int)m.R16(a + 0x50 + 2 * (uint)k);
                p.Gauge = (int)m.R8(a + 0x5A); p.Immersion = (int)m.R8(a + 0x5B);
                p.Job = (int)m.R8(a + 0x58); p.JobMastery = (int)m.R8(a + 0x59);
                p.InterestDay = (int)m.R16(a + 0x48); p.ArrowFlags = (int)m.R8(a + 0x69);
                p.PersonalityCode = (int)(m.R8(a + 0x32) & 0xF);
                int t = (int)m.R16(a + 0x80), i = (int)m.R16(a + 0x82);
                p.PlannedStateId = t == 0xFFFF ? "" : "orig:" + t + "," + i;
                p.PlannedTitle = t == 0xFFFF ? "" : Text.InterestTitle(t, i);
                uint g = OrigMem.Gene + OrigMem.GeneSize * (uint)id;
                p.FatherId = Rel(m.R16(g + 0x10)); p.MotherId = Rel(m.R16(g + 0x12)); p.SpouseId = Rel(m.R16(g + 0x14));
                p.Alive = true;
                p.Name = NameOf(id, p.Gender);
                f.Members.Add(p);
            }
            f.HeadId = (int)m.R16(0x0202C67C);
            OrigDate.Get(m, OrigMem.Date, out int y, out int mo, out int d);
            f.Today = SafeDay(y, mo, d);
            OrigDate.Get(m, 0x0202C688, out int sy, out int sm, out int sd);
            f.StartDay = sy > 0 ? SafeDay(sy, sm, sd) : f.Today;
        }

        static int SafeDay(int y, int mo, int d) { return GameDate.Make(Math.Max(1, Math.Min(9999, y)), Math.Max(1, Math.Min(12, mo)), Math.Max(1, d)); }

        static int Rel(uint v) { return v == 0xFFFF ? -1 : (int)v; }
    }
}

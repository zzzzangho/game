using System;
using System.Collections.Generic;
using System.Text;

namespace SennenKazoku.Core
{
    public sealed class ChoiceView { public string Id, Text; }

    public sealed class EventView
    {
        public string EventId, Title, Speaker, Text, Phase, Origin, Certainty, TextSource;
        public List<ChoiceView> Choices = new List<ChoiceView>();
        public bool NeedsChoice { get { return Phase == "choices"; } }
    }

    /// <summary>한국어 조사 처리: {self:은는} 형태.</summary>
    public static class Template
    {
        public static string Render(string text, Family f, Dictionary<string, int> cast)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                int a = text.IndexOf('{', i);
                if (a < 0) { sb.Append(text, i, text.Length - i); break; }
                int b = text.IndexOf('}', a);
                if (b < 0) { sb.Append(text, i, text.Length - i); break; }
                sb.Append(text, i, a - i);
                string tok = text.Substring(a + 1, b - a - 1);
                string role = tok, josa = null;
                int c = tok.IndexOf(':');
                if (c >= 0) { role = tok.Substring(0, c); josa = tok.Substring(c + 1); }
                int pid; Person p;
                if (cast.TryGetValue(role, out pid) && (p = f.Get(pid)) != null)
                {
                    sb.Append(p.Name);
                    if (josa != null) sb.Append(Josa(p.Name, josa));
                }
                else sb.Append(text, a, b - a + 1);
                i = b + 1;
            }
            return sb.ToString();
        }

        public static string Josa(string word, string kind)
        {
            if (string.IsNullOrEmpty(word)) return "";
            char last = word[word.Length - 1];
            int batchim = -1;
            if (last >= 0xAC00 && last <= 0xD7A3) batchim = (last - 0xAC00) % 28;
            bool has = batchim > 0;
            switch (kind)
            {
                case "은는": return has ? "은" : "는";
                case "이가": return has ? "이" : "가";
                case "을를": return has ? "을" : "를";
                case "과와": return has ? "과" : "와";
                case "으로": return (!has || batchim == 8) ? "로" : "으로";
            }
            return "";
        }
    }

    /// <summary>시간 진행 + 이벤트 선택 + 이벤트 진행. 코어 로직 전체가 UnityEngine 비의존.</summary>
    /// <summary>사건 하나로 바뀐 값. PersonId −1 = 가족 전체(무드).</summary>
    public sealed class EffectChange
    {
        public int PersonId; public string Key = ""; public int Delta;
        public string Label { get { switch (Key) { case "mood": return "무드"; case "hearts": return "하트"; case "s0": return "지력"; case "s1": return "체력"; case "s2": return "매력"; case "s3": return "운"; } return Key; } }
        public string Arrow { get { return Delta > 0 ? "↑" : "↓"; } }
        public static string Format(Family f, List<EffectChange> l)
        {
            var parts = new List<string>();
            foreach (var c in l) { var p = f.Get(c.PersonId); parts.Add((p != null ? p.Name + " " : "") + c.Label + c.Arrow); }
            return string.Join("  ", parts);
        }
    }

    public sealed class GameSession
    {
        public Family Family { get; private set; }
        public ContentCatalog Catalog { get; private set; }
        public readonly Rng Rng;
        public readonly List<string> Log = new List<string>();

        /// <summary>하루당 독립 이벤트 판정 확률 (임시 값: 원작의 발생 빈도는 미해명).</summary>
        public int EventRateNum = 1, EventRateDen = 14;

        readonly Dictionary<string, EventDef> snapshotCache = new Dictionary<string, EventDef>();

        public GameSession(Family f, ContentCatalog catalog)
        {
            Family = f; Catalog = catalog; Rng = new Rng(f.RngState);
        }

        /// <summary>팩 업데이트 후 카탈로그 교체. 진행 중 이벤트는 스냅샷을 쓰므로 영향 없음.</summary>
        public void ReplaceCatalog(ContentCatalog c) { Catalog = c; }

        public bool Paused { get { return Family.Active != null; } }

        // ---------- 하루 진행 ----------
        /// <summary>하루 진행. 이벤트가 진행 중이면 아무 것도 하지 않는다. 이벤트가 시작되면 true.</summary>
        public bool StepDay()
        {
            if (Family.Active != null) return false;
            if (Family.Queue.Count > 0) { StartNext(); return true; }
            Family.Today++;
            Interventions.Tick(Family);
            foreach (var p in Family.Members.ToArray())
            {
                if (!p.Alive) continue;
                TickPlanned(p);
            }
            if (Family.Queue.Count == 0) TryStandalone();
            Family.RngState = Rng.State;
            if (Family.Queue.Count > 0) { StartNext(); return true; }
            return false;
        }

        void TickPlanned(Person p)
        {
            int age = p.Age(Family.Today);
            if (string.IsNullOrEmpty(p.PlannedStateId))
            {
                var pick = PickState(p);
                if (pick != null)
                {
                    p.PlannedStateId = pick.Id;
                    p.PlannedDue = Family.Today + pick.DelayMin + Rng.Next(Math.Max(1, pick.DelayMax - pick.DelayMin + 1));
                }
                return;
            }
            if (p.PlannedDue > Family.Today) return;
            PlannedStateDef st;
            Catalog.States.TryGetValue(p.PlannedStateId, out st);
            p.PlannedStateId = ""; p.PlannedDue = -1;
            if (st == null) return; // 콘텐츠에서 제거된 상태: 조용히 해제
            foreach (var oid in st.Outcomes)
            {
                EventDef e; if (!Catalog.Events.TryGetValue(oid, out e)) continue;
                var cx = Resolve(e, p);
                if (cx == null || !Allowed(e, p)) continue;
                if (!Rules.Eval(e.Condition, cx)) continue;
                Enqueue(e, cx);
                return;                       // 처음 통과한 결과 하나만 선택
            }
        }

        PlannedStateDef PickState(Person p)
        {
            int age = p.Age(Family.Today);
            var cx = new RuleContext { Family = Family, Rng = Rng }; cx.Roles["self"] = p;
            var keys = new List<string>(Catalog.States.Keys); keys.Sort(StringComparer.Ordinal);   // 결정성
            var ok = new List<PlannedStateDef>(); int total = 0;
            foreach (var k in keys)
            {
                var s = Catalog.States[k];
                if (age < s.MinAge || age > s.MaxAge || !Rules.Eval(s.Eligible, cx)) continue;
                ok.Add(s); total += Math.Max(1, s.Weight);
            }
            if (total == 0) return null;
            int r = Rng.Next(total);
            foreach (var s in ok) { r -= Math.Max(1, s.Weight); if (r < 0) return s; }
            return null;
        }

        void TryStandalone()
        {
            if (!Rng.Chance(EventRateNum, EventRateDen)) return;
            var cands = new List<KeyValuePair<EventDef, RuleContext>>();
            var ids = new List<string>(Catalog.Events.Keys); ids.Sort(StringComparer.Ordinal);
            foreach (var p in Family.Members)
            {
                if (!p.Alive) continue;
                foreach (var id in ids)
                {
                    var e = Catalog.Events[id];
                    if (e.Kind != "standalone" || !Allowed(e, p)) continue;
                    var cx = Resolve(e, p);
                    if (cx == null || !Rules.Eval(e.Condition, cx)) continue;
                    cands.Add(new KeyValuePair<EventDef, RuleContext>(e, cx));
                }
            }
            if (cands.Count == 0) return;
            int top = int.MinValue;
            foreach (var c in cands) top = Math.Max(top, c.Key.Priority);
            int total = 0;
            foreach (var c in cands) if (c.Key.Priority == top) total += Math.Max(1, c.Key.Weight);
            int r = Rng.Next(total);
            foreach (var c in cands)
            {
                if (c.Key.Priority != top) continue;
                r -= Math.Max(1, c.Key.Weight);
                if (r < 0) { Enqueue(c.Key, c.Value); return; }
            }
        }

        RuleContext Resolve(EventDef e, Person self)
        {
            var cx = new RuleContext { Family = Family, Rng = Rng };
            cx.Roles["self"] = self;
            foreach (var r in e.Roles)
            {
                if (r.Key == "self") continue;
                int id = r.Value == "spouse" ? self.SpouseId : r.Value == "father" ? self.FatherId : self.MotherId;
                var q = id >= 0 ? Family.Get(id) : null;
                if (q == null || !q.Alive) return null;
                cx.Roles[r.Key] = q;
            }
            return cx;
        }

        static string Key(EventDef e, Person p) { return e.Id + "|" + p.Id; }

        bool Allowed(EventDef e, Person p)
        {
            int last, n;
            if (e.CooldownDays > 0 && Family.LastFired.TryGetValue(Key(e, p), out last) && Family.Today - last < e.CooldownDays) return false;
            if (e.MaxPerPerson > 0 && Family.FireCount.TryGetValue(Key(e, p), out n) && n >= e.MaxPerPerson) return false;
            if (e.MaxPerFamily > 0)
            {
                int total = 0;
                foreach (var m in Family.Members) if (Family.FireCount.TryGetValue(Key(e, m), out n)) total += n;
                if (total >= e.MaxPerFamily) return false;
            }
            return true;
        }

        void Enqueue(EventDef e, RuleContext cx)
        {
            var a = new ActiveEvent { EventId = e.Id, EventVersion = e.Version, SnapshotJson = MiniJson.Serialize(e.Raw) };
            foreach (var kv in cx.Roles) a.Cast[kv.Key] = kv.Value.Id;
            Family.Queue.Add(a);
        }

        void StartNext()
        {
            Family.Active = Family.Queue[0]; Family.Queue.RemoveAt(0);
            Family.Active.PageIndex = 0; Family.Active.Phase = "pages";
            Family.Active.Before = Snapshot();
            // 첫 페이지가 비어 있지 않도록 검증은 팩 로딩 단계에서 끝났다.
        }

        /// <summary>외부(디버그/에디터 미리보기)에서 특정 이벤트를 즉시 시작.</summary>
        public bool ForceStart(string eventId, int personId)
        {
            if (Family.Active != null) return false;
            EventDef e; var p = Family.Get(personId);
            if (p == null || !Catalog.Events.TryGetValue(eventId, out e)) return false;
            var cx = Resolve(e, p); if (cx == null) return false;
            Enqueue(e, cx); StartNext(); return true;
        }

        // ---------- 이벤트 진행 ----------
        EventDef Def(ActiveEvent a)
        {
            EventDef d;
            if (!snapshotCache.TryGetValue(a.SnapshotJson, out d))
            {
                d = EventDef.Parse(J.Obj(MiniJson.Parse(a.SnapshotJson)));
                snapshotCache[a.SnapshotJson] = d;
            }
            return d;
        }

        RuleContext CastContext(ActiveEvent a)
        {
            var cx = new RuleContext { Family = Family, Rng = Rng };
            foreach (var kv in a.Cast) { var p = Family.Get(kv.Value); if (p != null) cx.Roles[kv.Key] = p; }
            return cx;
        }

        List<PageDef> CurrentPages(ActiveEvent a, EventDef d)
        {
            if (a.Phase == "result")
                foreach (var c in d.Choices) if (c.Id == a.ChosenId) return c.Result;
            return d.Pages;
        }

        public EventView View()
        {
            var a = Family.Active; if (a == null) return null;
            var d = Def(a);
            var v = new EventView { EventId = a.EventId, Title = d.Title, Phase = a.Phase, Origin = d.Origin, Certainty = d.Certainty, TextSource = d.TextSource };
            if (a.Phase == "choices")
            {
                var cx = CastContext(a);
                foreach (var c in d.Choices)
                    if (Rules.Eval(c.Condition, cx))
                        v.Choices.Add(new ChoiceView { Id = c.Id, Text = Template.Render(c.Text, Family, a.Cast) });
                var last = d.Pages[d.Pages.Count - 1];
                v.Speaker = Name(a, last.Speaker); v.Text = Template.Render(last.Text, Family, a.Cast);
                return v;
            }
            var pages = CurrentPages(a, d);
            var pg = pages[Math.Min(a.PageIndex, pages.Count - 1)];
            v.Speaker = Name(a, pg.Speaker); v.Text = Template.Render(pg.Text, Family, a.Cast);
            return v;
        }

        string Name(ActiveEvent a, string role)
        {
            int id; Person p;
            return !string.IsNullOrEmpty(role) && a.Cast.TryGetValue(role, out id) && (p = Family.Get(id)) != null ? p.Name : "";
        }

        /// <summary>다음 페이지로. 마지막 페이지면 선택지 또는 종료로 넘어간다. 종료되면 true.</summary>
        public bool Advance()
        {
            var a = Family.Active; if (a == null) return false;
            if (a.Phase == "choices") return false;       // 선택 필요
            var d = Def(a);
            var pages = CurrentPages(a, d);
            if (a.PageIndex < pages.Count - 1) { a.PageIndex++; return false; }
            if (a.Phase == "pages" && VisibleChoiceCount(a, d) > 0) { a.Phase = "choices"; return false; }
            Finish(a, d);
            return true;
        }

        int VisibleChoiceCount(ActiveEvent a, EventDef d)
        {
            var cx = CastContext(a); int n = 0;
            foreach (var c in d.Choices) if (Rules.Eval(c.Condition, cx)) n++;
            return n;
        }

        public bool Choose(string choiceId)
        {
            var a = Family.Active; if (a == null || a.Phase != "choices") return false;
            var d = Def(a);
            var cx = CastContext(a);
            foreach (var c in d.Choices)
            {
                if (c.Id != choiceId || !Rules.Eval(c.Condition, cx)) continue;
                Rules.Apply(c.Effects, cx, Log);
                a.ChosenId = c.Id;
                if (c.Result.Count > 0) { a.Phase = "result"; a.PageIndex = 0; }
                else Finish(a, d);
                Family.RngState = Rng.State;
                return true;
            }
            return false;
        }

        // ---------- 사건 결과(무엇이 오르고 내렸나) ----------
        /// <summary>마지막으로 끝난 사건의 변화. 원작은 사건마다 "이번 일로 … 올랐어/내려갔어"로 알려 준다.</summary>
        public readonly List<EffectChange> LastChanges = new List<EffectChange>();

        Dictionary<string, int> Snapshot()
        {
            var d = new Dictionary<string, int> { { "mood", Family.Mood } };
            foreach (var p in Family.Members)
            {
                d["p" + p.Id + ".hearts"] = p.Hearts;
                for (int i = 0; i < 4; i++) d["p" + p.Id + ".s" + i] = p.Stats[i];
            }
            return d;
        }

        public static List<EffectChange> Diff(Family f, Dictionary<string, int> before)
        {
            var r = new List<EffectChange>();
            if (before == null || before.Count == 0) return r;
            int v;
            if (before.TryGetValue("mood", out v) && f.Mood != v) r.Add(new EffectChange { PersonId = -1, Key = "mood", Delta = f.Mood - v });
            foreach (var p in f.Members)
            {
                if (before.TryGetValue("p" + p.Id + ".hearts", out v) && p.Hearts != v) r.Add(new EffectChange { PersonId = p.Id, Key = "hearts", Delta = p.Hearts - v });
                for (int i = 0; i < 4; i++)
                    if (before.TryGetValue("p" + p.Id + ".s" + i, out v) && p.Stats[i] != v) r.Add(new EffectChange { PersonId = p.Id, Key = "s" + i, Delta = p.Stats[i] - v });
            }
            return r;
        }

        void Finish(ActiveEvent a, EventDef d)
        {
            var cx = CastContext(a);
            Rules.Apply(d.Effects, cx, Log);
            LastChanges.Clear(); LastChanges.AddRange(Diff(Family, a.Before));
            Person self = cx.Self;
            string key = d.Id + "|" + (self != null ? self.Id : -1);
            Family.LastFired[key] = Family.Today;
            int n; Family.FireCount.TryGetValue(key, out n); Family.FireCount[key] = n + 1;
            Family.History.Add(new EventRecord { Day = Family.Today, EventId = d.Id, EventVersion = d.Version, Title = d.Title,
                PersonId = self != null ? self.Id : -1, Choice = a.ChosenId, Changes = EffectChange.Format(Family, LastChanges) });
            Family.Active = null;
            Family.RngState = Rng.State;
        }
    }
}

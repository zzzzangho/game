using System;
using System.Collections.Generic;
using System.Text;

namespace SennenKazoku.Core
{
    public sealed class ChoiceView { public string Id, Text; }

    public sealed class EventView
    {
        public string EventId, Title, Speaker, Text, Phase, Origin, Certainty, TextSource;
        /// <summary>사건 인물 번호 (화면이 그 인물을 따라가게). −1 = 모름.</summary>
        public int PersonId = -1;
        public List<ChoiceView> Choices = new List<ChoiceView>();
        public bool NeedsChoice { get { return Phase == "choices"; } }
        /// <summary>원작 사건 장면 그림 (원작 세션만, 없으면 null) — 화면이 원작 그림(로컬 추출본)으로 그린다.</summary>
        public SceneView Scene;
    }

    /// <summary>
    /// 원작 사건 장면: 결과 기록 +0x20 제목 띠 · +0x24 뒤 무늬 · +0x28 액자 배경 (그림 이름 ev_band_/ev_back_/ev_pic_ + 포인터),
    /// 그리고 이 장(글상자)에서 액자 안에 선 인물과 동작(감정 말풍선) — 대사의 장면 토큰 1A 0E 로 정해진다(OrigText.ScenePages).
    /// </summary>
    public sealed class SceneView
    {
        public string Band = "", Back = "", Pic = "";
        public List<SceneActor> Actors = new List<SceneActor>();
    }

    /// <summary>
    /// 액자 안 인물: PersonId = 족보 번호(−1 = 가족이 아닌 사람), Anim = 동작 번호(0~0x14 감정 말풍선, 그 뒤는 자세).
    /// 가족이 아닌 사람은 NpcKind(1A 0E 04 첫 인자: 0·2 남 · 1·3 여) · NpcAge(둘째: 01 아기 · 02 어린이 · 06 노인 · 04/05/07 청년~어른) — 실기 스윕으로 확인.
    /// Outfit = 옷차림 인자(FF 기본, 00~06 다른 옷) — 앱 그림은 아직 옷을 바꾸지 않는다.
    /// </summary>
    public sealed class SceneActor { public int PersonId = -1; public int Anim = -1; public int NpcKind = -1, NpcAge = -1, Outfit = 0xFF; }

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

    public sealed class Prediction
    {
        public string OutcomeId = "", Title = "";
        public List<EffectChange> Changes = new List<EffectChange>();
        public bool HasRandom;
    }

    /// <summary>화면이 쓰는 진행 세션. GameSession = 팩 규칙(이전 방식), Orig.OrigSession = 원작 코드(변환 트리) 진행.</summary>
    public interface IGameSession
    {
        Family Family { get; }
        bool Paused { get; }
        bool StepDay();
        EventView View();
        bool Advance();
        bool Choose(string choiceId);
        Prediction Predict(int personId, bool max);
        void ReplaceCatalog(ContentCatalog c);
        /// <summary>저장 직전에 부른다 (원작 세션은 원작 메모리를 Family 에 적어 둔다).</summary>
        void PrepareSave();
        /// <summary>화살·아이템 쓰기. 성공하면 null, 못 쓰면 이유.</summary>
        string Use(Person target, string toolId);
        /// <summary>이 세션에서 쓸 수 있게 옮겨진 도구인가 (화면의 "미구현" 표시).</summary>
        bool CanUse(string toolId);
    }

    public sealed class GameSession : IGameSession
    {
        public Family Family { get; private set; }
        public string Use(Person target, string toolId) { return Interventions.Use(Family, target, toolId); }
        public bool CanUse(string toolId) { var t = Interventions.Find(toolId); return t != null && t.Implemented; }
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
        public void PrepareSave() { }

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

        /// <summary>
        /// 원작 인물별 하루 처리 중 관심사 부분 — ROM 0x08027E78 해독을 그대로 옮김.
        ///  ① 게이지 255 → MAX 사건, 0 → MIN 사건 (해당 요일 조건을 만족할 때만; 아니면 아무 일 없음)
        ///  ② 아니면 게이지 갱신: 힘내라 +32(상한 255) · 진정해 −64(하한 0) · 관심 날짜 &lt; T 이면 +(난수&amp;15)+4 · 아니면 −((난수&amp;31)+48)
        ///     T = 성격 코드와 관심사 유형이 같으면 12, 어느 쪽이 0 이면 11, 다르면 10 (ROM 0x080288A8)
        ///  ③ 관심 날짜 +1
        /// 사건이 일어나면 게이지 136 · 날짜 1 · 화살 표시 0 으로 새로 시작 (ROM 0x08028830/0x0802886A 관찰).
        /// 미해독(원작과 다를 수 있음): 사건이 실제로 일어나는 때를 정하는 인물 행동 루틴(0x08011D30 등 — 외출·귀가 상태), 다음 관심사 선택.
        /// </summary>
        void TickPlanned(Person p)
        {
            if (string.IsNullOrEmpty(p.PlannedStateId))
            {
                var pick = PickState(p);                 // 미해독: 원작의 다음 관심사 선택 (아래 PickState 주석)
                if (pick != null) { p.PlannedStateId = pick.Id; p.Gauge = 136; p.InterestDay = 1; p.ArrowFlags = 0; }
                return;
            }
            PlannedStateDef st;
            if (!Catalog.States.TryGetValue(p.PlannedStateId, out st)) { ClearInterest(p); return; }   // 콘텐츠에서 사라진 관심사
            if (p.Gauge >= 255 || p.Gauge <= 0)
            {
                bool max = p.Gauge >= 255;
                if (!DayAllowed(max ? st.MaxDayMode : st.MinDayMode, p.Job)) return;     // 그날은 사건 없음(게이지 유지)
                var list = max ? st.MaxOutcomes : st.MinOutcomes;
                ClearInterest(p);
                foreach (var oid in list)
                {
                    EventDef e; if (!Catalog.Events.TryGetValue(oid, out e)) continue;
                    var cx = Resolve(e, p);
                    if (cx == null || !Allowed(e, p)) continue;
                    if (!Rules.Eval(e.Condition, cx)) continue;
                    Enqueue(e, cx);
                    return;                       // 처음 통과한 결과 하나만 (확인됨: first_matching_variant_in_table_order)
                }
                return;
            }
            int t = ThresholdDays(p.PersonalityCode, st.Type);
            if ((p.ArrowFlags & 2) != 0) p.Gauge = Math.Min(255, p.Gauge + 32);
            else if ((p.ArrowFlags & 4) != 0) p.Gauge = Math.Max(0, p.Gauge - 64);
            else if (p.InterestDay < t) p.Gauge = Math.Min(255, p.Gauge + (int)(Rng.NextU32() & 15) + 4);
            else p.Gauge = Math.Max(0, p.Gauge - ((int)(Rng.NextU32() & 31) + 48));
            p.InterestDay++;
        }

        void ClearInterest(Person p)
        {
            p.PlannedStateId = ""; p.PlannedTitle = ""; p.PlannedDue = -1;
            p.Gauge = 136; p.InterestDay = 1; p.ArrowFlags = 0; p.ArrowId = "";
        }

        /// <summary>ROM 0x080288A8: 성격 코드(a)·관심사 유형(b) → 게이지가 오르는 날 수.</summary>
        public static int ThresholdDays(int a, int b) { return a == 0 || b == 0 ? 11 : a == b ? 12 : 10; }

        /// <summary>
        /// MAX/MIN 사건이 그날 가능한가 (ROM 0x080281E8~). mode 3 = 아무 날, 2 = 직업의 근무 요일 비트, 1 = 그 반대.
        /// 요일 비트는 bit0 = 일요일. 직업 요일표가 팩에 없으면(원작 표 미추출) 아무 날로 본다.
        /// </summary>
        bool DayAllowed(int mode, int job)
        {
            int mask;
            if (mode == 3 || !Catalog.JobDayMasks.TryGetValue(job, out mask)) mask = 0x7F;
            else if (mode == 1) mask ^= 0x7F;
            return (mask & (1 << GameDate.Weekday(Family.Today))) != 0;
        }

        /// <summary>
        /// 미해독: 원작에서 사건 직후 바로 다음 관심사가 정해진다(관찰). 정하는 루틴은 아직 해독하지 못해
        /// 팩의 대상 조건(eligible)·나이로 고른다 — 이 부분은 원작 규칙이 아니다.
        /// </summary>
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

        Dictionary<string, int> Snapshot() { return Snapshot(Family); }

        static Dictionary<string, int> Snapshot(Family family)
        {
            var d = new Dictionary<string, int> { { "mood", family.Mood } };
            foreach (var p in family.Members)
            {
                d["p" + p.Id + ".hearts"] = p.Hearts;
                for (int i = 0; i < 4; i++) d["p" + p.Id + ".s" + i] = p.Stats[i];
            }
            return d;
        }

        /// <summary>
        /// 지금 관심사가 이루어지면 무엇이 오르고 내리는지 미리 본다(원작: 푹 빠져 있을 때 보여 주는 결과 예고).
        /// 가족을 복제해 같은 규칙(표 순서 첫 통과 결과)으로 결과를 골라 효과를 적용해 본 뒤 차이를 돌려준다. 실제 가족은 바뀌지 않는다.
        /// 조건에 난수가 섞인 결과는 지금 난수 상태 기준의 한 가지 경우다(HasRandom).
        /// </summary>
        public Prediction Predict(int personId) { return Predict(personId, true); }

        /// <param name="max">true = 게이지 255 에서 검사하는 MAX 목록, false = 0 에서의 MIN 목록</param>
        public Prediction Predict(int personId, bool max)
        {
            var p = Family.Get(personId);
            if (p == null || string.IsNullOrEmpty(p.PlannedStateId)) return null;
            PlannedStateDef st; if (!Catalog.States.TryGetValue(p.PlannedStateId, out st)) return null;
            var clone = SaveSystem.FromJson(J.Obj(MiniJson.Parse(MiniJson.Serialize(SaveSystem.ToJson(Family, Catalog, SaveSystem.CurrentSchema)))));
            var sim = new GameSession(clone, Catalog); sim.Rng.State = Rng.State;
            var cp = clone.Get(personId);
            foreach (var oid in max ? st.MaxOutcomes : st.MinOutcomes)
            {
                EventDef e; if (!Catalog.Events.TryGetValue(oid, out e)) continue;
                var cx = sim.Resolve(e, cp);
                if (cx == null || !sim.Allowed(e, cp)) continue;
                if (!Rules.Eval(e.Condition, cx)) continue;
                var before = Snapshot(clone);
                Rules.Apply(e.Effects, cx, new List<string>());
                var json = MiniJson.Serialize(e.Raw);
                return new Prediction { OutcomeId = e.Id, Title = e.Title, Changes = Diff(clone, before),
                    HasRandom = json.Contains("\"random\"") || json.Contains("\"chance\"") || json.Contains("\"skill_check\"") };
            }
            return new Prediction { OutcomeId = "", Title = "", Changes = new List<EffectChange>() };
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

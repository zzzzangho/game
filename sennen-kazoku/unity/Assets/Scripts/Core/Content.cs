using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SennenKazoku.Core
{
    public sealed class PageDef { public string Speaker = ""; public string Text = ""; }

    public sealed class ChoiceDef
    {
        public string Id = "", Text = "";
        public object Condition;
        public List<object> Effects = new List<object>();
        public List<PageDef> Result = new List<PageDef>();
    }

    public sealed class EventDef
    {
        public string Id = "", Title = "", Category = "", SourceRef = "";
        public int Version = 1;
        public string Origin = "new";            // original | new
        public string Certainty = "placeholder"; // confirmed | estimated | placeholder  (규칙의 근거 수준)
        public string TextSource = "placeholder";// original-translation | placeholder | new
        public string Kind = "standalone";       // standalone | outcome
        public Dictionary<string, string> Roles = new Dictionary<string, string>();
        public object Condition;
        public int Weight = 10, Priority, CooldownDays, MaxPerPerson, MaxPerFamily;
        public List<PageDef> Pages = new List<PageDef>();
        public List<ChoiceDef> Choices = new List<ChoiceDef>();
        public List<object> Effects = new List<object>();
        public string PackId = "";
        public Dictionary<string, object> Raw;

        static List<PageDef> ParsePages(List<object> l)
        {
            var r = new List<PageDef>();
            foreach (var o in l) { var d = J.Obj(o); r.Add(new PageDef { Speaker = J.Str(d, "speaker"), Text = J.Str(d, "text") }); }
            return r;
        }

        public static EventDef Parse(Dictionary<string, object> d)
        {
            var e = new EventDef {
                Raw = d, Id = J.Str(d, "id"), Version = J.Int(d, "version", 1), Title = J.Str(d, "title"),
                Category = J.Str(d, "category"), SourceRef = J.Str(d, "sourceRef"), Origin = J.Str(d, "origin", "new"),
                Certainty = J.Str(d, "certainty", "placeholder"), TextSource = J.Str(d, "textSource", "placeholder"),
                Kind = J.Str(d, "kind", "standalone"), Effects = J.List(d, "effects")
            };
            var roles = J.Child(d, "roles");
            if (roles != null) foreach (var kv in roles) e.Roles[kv.Key] = (string)kv.Value;
            if (!e.Roles.ContainsKey("self")) e.Roles["self"] = "candidate";
            var t = J.Child(d, "trigger");
            if (t != null)
            {
                e.Condition = J.Get(t, "condition");
                e.Weight = J.Int(t, "weight", 10); e.Priority = J.Int(t, "priority", 0);
                e.CooldownDays = J.Int(t, "cooldownDays", 0); e.MaxPerPerson = J.Int(t, "maxPerPerson", 0);
                e.MaxPerFamily = J.Int(t, "maxPerFamily", 0);
            }
            e.Pages = ParsePages(J.List(d, "pages"));
            foreach (var o in J.List(d, "choices"))
            {
                var c = J.Obj(o);
                e.Choices.Add(new ChoiceDef { Id = J.Str(c, "id"), Text = J.Str(c, "text"), Condition = J.Get(c, "condition"),
                    Effects = J.List(c, "effects"), Result = ParsePages(J.List(c, "result")) });
            }
            return e;
        }

        public List<string> Validate()
        {
            var err = new List<string>();
            string p = "event[" + Id + "]";
            if (string.IsNullOrEmpty(Id)) err.Add(p + ": id 없음");
            if (Version < 1) err.Add(p + ": version >= 1 필요");
            if (Origin != "original" && Origin != "new") err.Add(p + ": origin 은 original|new");
            if (Kind != "standalone" && Kind != "outcome") err.Add(p + ": kind 는 standalone|outcome");
            if (Pages.Count == 0) err.Add(p + ": pages 비어 있음");
            foreach (var r in Roles)
            {
                if (r.Key == "self") continue;
                if (!Capabilities.Roles.Contains(r.Key) || (r.Value != "spouse" && r.Value != "father" && r.Value != "mother"))
                    err.Add(p + ": roles." + r.Key + " 정의 오류 (self 외에는 spouse/father/mother 관계만 지원)");
            }
            if (Condition != null) Rules.ValidateCondition(Condition, p + ".trigger.condition", err);
            Rules.ValidateEffects(Effects, p + ".effects", err);
            var seen = new HashSet<string>();
            foreach (var c in Choices)
            {
                if (string.IsNullOrEmpty(c.Id) || !seen.Add(c.Id)) err.Add(p + ": 선택지 id 누락/중복 '" + c.Id + "'");
                if (c.Condition != null) Rules.ValidateCondition(c.Condition, p + ".choice[" + c.Id + "].condition", err);
                Rules.ValidateEffects(c.Effects, p + ".choice[" + c.Id + "].effects", err);
            }
            return err;
        }
    }

    public sealed class PlannedStateDef
    {
        public string Id = "", Title = "", PackId = "", Desc = "";
        public int MinAge, MaxAge = 120, Weight = 10, DelayMin = 20, DelayMax = 60;
        public List<string> Outcomes = new List<string>();   // 표 순서대로 검사, 처음 통과한 것을 선택 (확인됨: first_matching_variant_in_table_order)
        public string Certainty = "placeholder";
        public object Eligible;                               // 배정 대상 조건(나이·성별·혼인 등). 원작의 "대상 분류"에 해당

        public static PlannedStateDef Parse(Dictionary<string, object> d)
        {
            var s = new PlannedStateDef { Id = J.Str(d, "id"), Title = J.Str(d, "title"), MinAge = J.Int(d, "minAge", 0),
                MaxAge = J.Int(d, "maxAge", 120), Weight = J.Int(d, "weight", 10), Certainty = J.Str(d, "certainty", "placeholder"), Eligible = J.Get(d, "eligible"), Desc = J.Str(d, "desc") };
            var dl = J.List(d, "delay");
            if (dl.Count == 2) { s.DelayMin = Convert.ToInt32(dl[0]); s.DelayMax = Convert.ToInt32(dl[1]); }
            foreach (var o in J.List(d, "outcomes")) s.Outcomes.Add((string)o);
            return s;
        }
    }

    /// <summary>맵(사는 곳). 그림은 Art 의 이미지 이름, 방은 x 구간(그림 px)과 용도(kind)로 정의 → 새 맵은 팩 데이터로 추가.</summary>
    public sealed class MapDef
    {
        public static readonly HashSet<string> Kinds = new HashSet<string> { "entrance", "bedroom", "master", "living", "kitchen", "bath", "study", "yard", "outside" };
        public sealed class Room { public string Id = "", Name = "", Kind = ""; public int X0, X1; }
        public string Id = "", Name = "", Art = "", PackId = "";
        public int Width, Height = 160, FloorY = 112; public bool Loops = true;
        public List<Room> Rooms = new List<Room>();

        public static MapDef Parse(Dictionary<string, object> d)
        {
            var m = new MapDef { Id = J.Str(d, "id"), Name = J.Str(d, "name"), Art = J.Str(d, "art"), Width = J.Int(d, "width"), Height = J.Int(d, "height", 160),
                FloorY = J.Int(d, "floorY", 112), Loops = J.Bool(d, "loops", true) };
            foreach (var o in J.List(d, "rooms"))
            {
                var r = J.Obj(o); var x = J.List(r, "x");
                m.Rooms.Add(new Room { Id = J.Str(r, "id"), Name = J.Str(r, "name"), Kind = J.Str(r, "kind"),
                    X0 = x.Count == 2 ? Convert.ToInt32(x[0]) : 0, X1 = x.Count == 2 ? Convert.ToInt32(x[1]) : 0 });
            }
            return m;
        }

        public List<string> Validate()
        {
            var e = new List<string>(); string p = "map[" + Id + "]";
            if (string.IsNullOrEmpty(Id) || string.IsNullOrEmpty(Art)) e.Add(p + ": id/art 필요");
            if (Width <= 0) e.Add(p + ": width 필요");
            if (Rooms.Count == 0) e.Add(p + ": 방이 없음");
            var ids = new HashSet<string>();
            for (int i = 0; i < Rooms.Count; i++)
            {
                var r = Rooms[i];
                if (!ids.Add(r.Id)) e.Add(p + ": 방 id 중복 " + r.Id);
                if (!Kinds.Contains(r.Kind)) e.Add(p + ": 알 수 없는 방 용도 '" + r.Kind + "' (앱 업데이트 필요)");
                if (r.X0 < 0 || r.X1 > Width || r.X1 - r.X0 < 40) e.Add(p + ": 방 " + r.Id + " 범위 오류(폭 40 이상, 맵 안)");
                for (int k = 0; k < i; k++) if (r.X0 < Rooms[k].X1 && Rooms[k].X0 < r.X1) e.Add(p + ": 방 " + r.Id + " 와 " + Rooms[k].Id + " 겹침");
            }
            return e;
        }

        public Room FirstOfKind(string kind) { foreach (var r in Rooms) if (r.Kind == kind) return r; return null; }
    }

    public sealed class Pack
    {
        public string PackId = "", Title = "", Kind = "expansion", Origin = "new";
        public int Version, MinContract = 1;
        public List<KeyValuePair<string, int>> Requires = new List<KeyValuePair<string, int>>();
        public List<EventDef> Events = new List<EventDef>();
        public List<PlannedStateDef> States = new List<PlannedStateDef>();
        public List<MapDef> Maps = new List<MapDef>();
        public string Sha256 = "";
        public string RawJson = "";

        /// <summary>팩 파싱과 단독 검증. 실패 시 errors 에 이유를 채우고 null.</summary>
        public static Pack Load(string json, List<string> errors)
        {
            Dictionary<string, object> d;
            try { d = J.Obj(MiniJson.Parse(json)); }
            catch (Exception ex) { errors.Add("JSON 파싱 실패: " + ex.Message); return null; }
            if (d == null) { errors.Add("최상위가 객체가 아님"); return null; }
            if (J.Int(d, "format") != 1) { errors.Add("지원하지 않는 팩 format: " + J.Int(d, "format")); return null; }
            var p = new Pack { PackId = J.Str(d, "packId"), Title = J.Str(d, "title"), Kind = J.Str(d, "kind", "expansion"),
                Origin = J.Str(d, "origin", "new"), Version = J.Int(d, "version"), MinContract = J.Int(d, "minContract", 1), RawJson = json };
            p.Sha256 = Hash(json);
            if (string.IsNullOrEmpty(p.PackId)) errors.Add("packId 없음");
            if (p.Version < 1) errors.Add("version >= 1 필요");
            if (p.MinContract > Capabilities.Contract)
                errors.Add("이 팩은 데이터 계약 v" + p.MinContract + " 필요 (앱은 v" + Capabilities.Contract + ") — 앱 업데이트 필요");
            foreach (var r in J.List(d, "requires")) { var o = J.Obj(r); p.Requires.Add(new KeyValuePair<string, int>(J.Str(o, "packId"), J.Int(o, "minVersion", 1))); }
            var ids = new HashSet<string>();
            foreach (var o in J.List(d, "events"))
            {
                var e = EventDef.Parse(J.Obj(o)); e.PackId = p.PackId;
                if (!ids.Add(e.Id)) errors.Add("팩 내 이벤트 id 중복: " + e.Id);
                errors.AddRange(e.Validate());
                p.Events.Add(e);
            }
            foreach (var o in J.List(d, "plannedStates"))
            {
                var s = PlannedStateDef.Parse(J.Obj(o)); s.PackId = p.PackId;
                if (string.IsNullOrEmpty(s.Id)) errors.Add("plannedState id 없음");
                if (s.Outcomes.Count == 0) errors.Add("plannedState[" + s.Id + "]: outcomes 비어 있음");
                if (s.Eligible != null) Rules.ValidateCondition(s.Eligible, "plannedState[" + s.Id + "].eligible", errors);
                p.States.Add(s);
            }
            foreach (var o in J.List(d, "maps"))
            {
                var m = MapDef.Parse(J.Obj(o)); m.PackId = p.PackId; errors.AddRange(m.Validate()); p.Maps.Add(m);
            }
            return errors.Count == 0 ? p : null;
        }

        public static string Hash(string json)
        {
            using (var sha = SHA256.Create())
            {
                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                var sb = new StringBuilder();
                foreach (var x in b) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>활성 팩들을 합친 읽기 전용 콘텐츠 카탈로그.</summary>
    public sealed class ContentCatalog
    {
        public readonly Dictionary<string, EventDef> Events = new Dictionary<string, EventDef>();
        public readonly Dictionary<string, PlannedStateDef> States = new Dictionary<string, PlannedStateDef>();
        public readonly Dictionary<string, MapDef> Maps = new Dictionary<string, MapDef>();
        public readonly List<Pack> Packs = new List<Pack>();

        /// <summary>팩 집합 전체의 교차 검증(id 중복, 참조, 의존). 오류가 있으면 null.</summary>
        public static ContentCatalog Build(List<Pack> packs, List<string> errors)
        {
            var c = new ContentCatalog();
            var byId = new Dictionary<string, Pack>();
            foreach (var p in packs)
            {
                if (byId.ContainsKey(p.PackId)) { errors.Add("팩 중복: " + p.PackId); continue; }
                byId[p.PackId] = p;
            }
            foreach (var p in packs)
            {
                foreach (var r in p.Requires)
                {
                    Pack q;
                    if (!byId.TryGetValue(r.Key, out q) || q.Version < r.Value)
                        errors.Add("팩 " + p.PackId + " 는 " + r.Key + " v" + r.Value + " 이상이 필요함");
                }
                foreach (var e in p.Events)
                {
                    if (c.Events.ContainsKey(e.Id)) errors.Add("이벤트 id 충돌: " + e.Id + " (" + c.Events[e.Id].PackId + " / " + p.PackId + ")");
                    else c.Events[e.Id] = e;
                }
                foreach (var m in p.Maps)
                {
                    if (c.Maps.ContainsKey(m.Id)) errors.Add("맵 id 충돌: " + m.Id); else c.Maps[m.Id] = m;
                }
                foreach (var s in p.States)
                {
                    if (c.States.ContainsKey(s.Id)) errors.Add("예정 상태 id 충돌: " + s.Id);
                    else c.States[s.Id] = s;
                }
                c.Packs.Add(p);
            }
            foreach (var s in c.States.Values)
                foreach (var o in s.Outcomes)
                    if (!c.Events.ContainsKey(o)) errors.Add("예정 상태 " + s.Id + " 의 결과 이벤트가 없음: " + o);
            return errors.Count == 0 ? c : null;
        }
    }

    // ---------- 디스크 저장소: 검증 후 적용, 실패 시 롤백 ----------
    public sealed class InstallResult
    {
        public bool Ok; public string Message = ""; public List<string> Errors = new List<string>();
    }

    /// <summary>
    /// 다운로드/번들 팩 관리. 레이아웃: root/packs/&lt;packId&gt;/&lt;version&gt;.json, root/active.json.
    /// 새 팩은 해시 → 단독 검증 → 활성 집합과의 교차 검증을 모두 통과해야 active.json 에 반영된다.
    /// active.json 은 임시 파일에 쓴 뒤 교체한다. 이전 버전 파일은 보존되어 로딩 실패 시 되돌아간다.
    /// </summary>
    public sealed class PackStore
    {
        readonly string root;
        public PackStore(string root) { this.root = root; Directory.CreateDirectory(Path.Combine(root, "packs")); }
        string PackFile(string id, int ver) { return Path.Combine(root, "packs", id, ver + ".json"); }
        string ActiveFile { get { return Path.Combine(root, "active.json"); } }

        public Dictionary<string, int> ReadActive()
        {
            var r = new Dictionary<string, int>();
            try
            {
                if (!File.Exists(ActiveFile)) return r;
                var d = J.Obj(MiniJson.Parse(File.ReadAllText(ActiveFile, Encoding.UTF8)));
                foreach (var kv in J.Child(d, "packs")) r[kv.Key] = Convert.ToInt32(kv.Value);
            }
            catch (Exception) { /* 손상: 빈 목록으로 간주 → 번들만 사용 */ }
            return r;
        }

        void WriteActive(Dictionary<string, int> a)
        {
            var packs = new Dictionary<string, object>();
            foreach (var kv in a) packs[kv.Key] = kv.Value;
            string tmp = ActiveFile + ".tmp";
            File.WriteAllText(tmp, MiniJson.Serialize(new Dictionary<string, object> { { "packs", packs } }), new UTF8Encoding(false));
            if (File.Exists(ActiveFile)) File.Replace(tmp, ActiveFile, null); else File.Move(tmp, ActiveFile);
        }

        /// <summary>다운로드한 팩 설치. 어떤 단계든 실패하면 기존 상태를 그대로 둔다.</summary>
        public InstallResult Install(string json, string expectedSha256, List<Pack> bundled)
        {
            var res = new InstallResult();
            if (!string.IsNullOrEmpty(expectedSha256) && !string.Equals(Pack.Hash(json), expectedSha256, StringComparison.OrdinalIgnoreCase))
            { res.Message = "해시 불일치"; res.Errors.Add("sha256 불일치 — 손상되었거나 변조된 파일"); return res; }
            var errs = new List<string>();
            var pack = Pack.Load(json, errs);
            if (pack == null) { res.Message = "팩 검증 실패"; res.Errors = errs; return res; }

            var active = ReadActive();
            int cur;
            int bundledVer = 0;
            foreach (var b in bundled) if (b.PackId == pack.PackId) bundledVer = b.Version;
            if (active.TryGetValue(pack.PackId, out cur) ? pack.Version <= cur : pack.Version <= bundledVer)
            { res.Message = "이미 같거나 더 새로운 버전 사용 중"; res.Errors.Add("version " + pack.Version + " <= 현재"); return res; }

            // 교차 검증: 후보 활성 집합으로 카탈로그를 만들어 본다.
            var next = new Dictionary<string, int>(active); next[pack.PackId] = pack.Version;
            var candidate = new List<Pack>();
            var loadErr = new List<string>();
            foreach (var b in bundled) if (!next.ContainsKey(b.PackId)) candidate.Add(b);
            foreach (var kv in next)
            {
                if (kv.Key == pack.PackId) { candidate.Add(pack); continue; }
                var lp = LoadInstalled(kv.Key, kv.Value, loadErr);
                if (lp != null) candidate.Add(lp);
            }
            var cerr = new List<string>(loadErr);
            if (ContentCatalog.Build(candidate, cerr) == null) { res.Message = "기존 콘텐츠와 충돌"; res.Errors = cerr; return res; }

            try
            {
                string file = PackFile(pack.PackId, pack.Version);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, json, new UTF8Encoding(false));
                WriteActive(next);
            }
            catch (Exception ex) { res.Message = "저장 실패: " + ex.Message; res.Errors.Add(ex.Message); return res; }
            res.Ok = true; res.Message = pack.PackId + " v" + pack.Version + " 적용"; return res;
        }

        Pack LoadInstalled(string id, int ver, List<string> errors)
        {
            string f = PackFile(id, ver);
            if (!File.Exists(f)) { errors.Add("설치 파일 없음: " + id + " v" + ver); return null; }
            var e = new List<string>();
            var p = Pack.Load(File.ReadAllText(f, Encoding.UTF8), e);
            if (p == null) { errors.Add(id + " v" + ver + ": " + string.Join("; ", e)); return null; }
            return p;
        }

        /// <summary>
        /// 시작 시 카탈로그 구성: 번들 팩 + 설치 팩(번들보다 높은 버전). 설치 팩이 손상/충돌이면 같은 팩의
        /// 이전 설치 버전 → 번들 버전 순으로 되돌아간다. 모든 문제는 warnings 로 보고한다.
        /// </summary>
        public ContentCatalog LoadCatalog(List<Pack> bundled, List<string> warnings)
        {
            var active = ReadActive();
            var chosen = new Dictionary<string, Pack>();
            foreach (var b in bundled) chosen[b.PackId] = b;
            foreach (var kv in active)
            {
                var errs = new List<string>();
                Pack best = null;
                // 지정 버전부터 아래로 내려가며 가장 높은 유효 버전을 찾는다.
                var vers = ListVersions(kv.Key);
                vers.Sort(); vers.Reverse();
                foreach (var v in vers)
                {
                    if (v > kv.Value) continue;
                    var p = LoadInstalled(kv.Key, v, errs);
                    if (p != null) { best = p; break; }
                }
                foreach (var m in errs) warnings.Add(m);
                Pack bun; chosen.TryGetValue(kv.Key, out bun);
                if (best != null && (bun == null || best.Version > bun.Version)) chosen[kv.Key] = best;
                else if (best == null && bun == null) warnings.Add("팩 " + kv.Key + " 를 불러올 수 없음");
            }
            var list = new List<Pack>(chosen.Values);
            var cerr = new List<string>();
            var cat = ContentCatalog.Build(list, cerr);
            if (cat != null) return cat;
            // 교차 검증 실패: 번들 팩만으로 되돌아간다.
            foreach (var m in cerr) warnings.Add("교차 검증 실패 → 번들 콘텐츠로 복귀: " + m);
            var e2 = new List<string>();
            return ContentCatalog.Build(bundled, e2);
        }

        List<int> ListVersions(string id)
        {
            var r = new List<int>();
            string dir = Path.Combine(root, "packs", id);
            if (!Directory.Exists(dir)) return r;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            { int v; if (int.TryParse(Path.GetFileNameWithoutExtension(f), out v)) r.Add(v); }
            return r;
        }
    }
}

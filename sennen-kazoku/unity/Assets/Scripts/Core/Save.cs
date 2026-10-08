using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SennenKazoku.Core
{
    public sealed class SaveMeta
    {
        public string Slot = "", FamilyName = "", SavedAt = ""; public int Day, Members, Schema;
        public bool Corrupt;
    }

    public sealed class LoadResult
    {
        public Family Family; public string Warning = ""; public bool UsedBackup; public Dictionary<string, int> PackVersions = new Dictionary<string, int>();
        public int FromSchema;
    }

    /// <summary>저장 포맷 마이그레이션 체인. 각 단계는 from → from+1 로 JSON 트리를 변환한다.</summary>
    public sealed class SaveMigrator
    {
        public int Current { get; private set; }
        readonly Dictionary<int, Func<Dictionary<string, object>, Dictionary<string, object>>> steps =
            new Dictionary<int, Func<Dictionary<string, object>, Dictionary<string, object>>>();
        public SaveMigrator(int current) { Current = current; }
        public void Register(int from, Func<Dictionary<string, object>, Dictionary<string, object>> fn) { steps[from] = fn; }

        public Dictionary<string, object> Migrate(Dictionary<string, object> root)
        {
            int v = J.Int(root, "schema", 0);
            if (v > Current) throw new InvalidDataException("더 새로운 앱에서 만든 저장 (schema " + v + " > " + Current + "). 앱을 업데이트하세요.");
            while (v < Current)
            {
                Func<Dictionary<string, object>, Dictionary<string, object>> fn;
                if (!steps.TryGetValue(v, out fn)) throw new InvalidDataException("마이그레이션 경로 없음: schema " + v + " → " + (v + 1));
                root = fn(root); v++; root["schema"] = v;
            }
            return root;
        }
    }

    public sealed class SaveSystem
    {
        /// <summary>현재 저장 스키마. 저장 구조를 바꿀 때마다 올리고 SaveMigrator 단계를 추가한다.</summary>
        public const int CurrentSchema = 2;
        public static readonly string[] Slots = { "auto", "slot1", "slot2", "slot3" };

        readonly string dir;
        public readonly SaveMigrator Migrator = DefaultMigrator();
        public SaveSystem(string dir) { this.dir = dir; Directory.CreateDirectory(dir); }

        /// <summary>실제 마이그레이션 체인. v1→v2: 가족 시작일·세대주·신님에게 감사·화살/아이템 보유(원작 시작값 각 5) 추가.</summary>
        public static SaveMigrator DefaultMigrator()
        {
            var m = new SaveMigrator(CurrentSchema);
            m.Register(1, root =>
            {
                var fam = J.Child(root, "family");
                if (fam != null)
                {
                    if (!fam.ContainsKey("startDay")) fam["startDay"] = J.Int(fam, "today");
                    if (!fam.ContainsKey("gratitude")) fam["gratitude"] = 0;
                    if (!fam.ContainsKey("head")) { var ms = J.List(fam, "members"); fam["head"] = ms.Count > 0 ? J.Int(J.Obj(ms[0]), "id") : -1; }
                    if (!fam.ContainsKey("items")) fam["items"] = new Dictionary<string, object> { { "arrow.encourage", 5L }, { "arrow.calm", 5L } };
                }
                return root;
            });
            return m;
        }
        public SaveSystem(string dir, SaveMigrator m) { this.dir = dir; Directory.CreateDirectory(dir); Migrator = m; }

        string PathOf(string slot) { return Path.Combine(dir, "save_" + slot + ".json"); }

        // ---------- 직렬화 ----------
        public static Dictionary<string, object> ToJson(Family f, ContentCatalog cat, int schema)
        {
            var members = new List<object>(); foreach (var p in f.Members) members.Add(p.ToJson());
            var hist = new List<object>();
            foreach (var h in f.History)
                hist.Add(new Dictionary<string, object> { { "day", h.Day }, { "id", h.EventId }, { "ver", h.EventVersion }, { "title", h.Title }, { "person", h.PersonId }, { "choice", h.Choice }, { "changes", h.Changes } });
            var last = new Dictionary<string, object>(); foreach (var kv in f.LastFired) last[kv.Key] = kv.Value;
            var cnt = new Dictionary<string, object>(); foreach (var kv in f.FireCount) cnt[kv.Key] = kv.Value;
            var queue = new List<object>(); foreach (var q in f.Queue) queue.Add(q.ToJson());
            var flags = new List<object>(); foreach (var s in f.Flags) flags.Add(s);
            var packs = new Dictionary<string, object>();
            if (cat != null) foreach (var p in cat.Packs) packs[p.PackId] = p.Version;
            return new Dictionary<string, object> {
                { "schema", schema }, { "savedAt", DateTime.UtcNow.ToString("o") },
                { "packs", packs },
                { "family", new Dictionary<string, object> {
                    { "name", f.Name }, { "today", f.Today }, { "mood", f.Mood }, { "assets", f.Assets }, { "house", f.HouseGrade },
                    { "nextPersonId", f.NextPersonId }, { "members", members }, { "flags", flags }, { "history", hist },
                    { "lastFired", last }, { "fireCount", cnt }, { "rng", f.RngState.ToString() },
                    { "active", f.Active != null ? f.Active.ToJson() : null }, { "queue", queue },
                    { "startDay", f.StartDay }, { "gratitude", f.Gratitude }, { "head", f.HeadId }, { "items", ItemsJson(f) } } }
            };
        }

        static Dictionary<string, object> ItemsJson(Family f)
        {
            var d = new Dictionary<string, object>(); foreach (var kv in f.Items) d[kv.Key] = kv.Value; return d;
        }

        public static Family FromJson(Dictionary<string, object> root)
        {
            var d = J.Child(root, "family");
            if (d == null) throw new InvalidDataException("family 항목 없음");
            var f = new Family { Name = J.Str(d, "name"), Today = J.Int(d, "today"), Mood = J.Int(d, "mood", 128), Assets = J.Long(d, "assets"),
                HouseGrade = J.Int(d, "house"), NextPersonId = J.Int(d, "nextPersonId", 1) };
            foreach (var o in J.List(d, "members")) f.Members.Add(Person.FromJson(J.Obj(o)));
            foreach (var o in J.List(d, "flags")) f.Flags.Add((string)o);
            foreach (var o in J.List(d, "history"))
            {
                var h = J.Obj(o);
                f.History.Add(new EventRecord { Day = J.Int(h, "day"), EventId = J.Str(h, "id"), EventVersion = J.Int(h, "ver"), Title = J.Str(h, "title"), PersonId = J.Int(h, "person", -1), Choice = J.Str(h, "choice"), Changes = J.Str(h, "changes") });
            }
            var lf = J.Child(d, "lastFired"); if (lf != null) foreach (var kv in lf) f.LastFired[kv.Key] = Convert.ToInt32(kv.Value);
            var fc = J.Child(d, "fireCount"); if (fc != null) foreach (var kv in fc) f.FireCount[kv.Key] = Convert.ToInt32(kv.Value);
            ulong rng; if (ulong.TryParse(J.Str(d, "rng"), out rng)) f.RngState = rng;
            var act = J.Child(d, "active"); if (act != null) f.Active = ActiveEvent.FromJson(act);
            foreach (var o in J.List(d, "queue")) f.Queue.Add(ActiveEvent.FromJson(J.Obj(o)));
            f.StartDay = J.Int(d, "startDay", f.Today); f.Gratitude = J.Int(d, "gratitude"); f.HeadId = J.Int(d, "head", -1);
            var it = J.Child(d, "items"); if (it != null) foreach (var kv in it) f.Items[kv.Key] = Convert.ToInt32(kv.Value);
            return f;
        }

        // ---------- 파일 ----------
        /// <summary>임시 파일에 먼저 쓰고 교체한다. 직전 정상본은 .bak 으로 남긴다.</summary>
        public void Save(string slot, Family f, ContentCatalog cat)
        {
            string path = PathOf(slot), tmp = path + ".tmp";
            string json = MiniJson.Serialize(ToJson(f, cat, Migrator.Current), true);
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak"); else File.Move(tmp, path);
        }

        public bool Exists(string slot) { return File.Exists(PathOf(slot)) || File.Exists(PathOf(slot) + ".bak"); }
        public void Delete(string slot) { foreach (var s in new[] { "", ".bak", ".tmp" }) if (File.Exists(PathOf(slot) + s)) File.Delete(PathOf(slot) + s); }

        public LoadResult Load(string slot)
        {
            var warn = new StringBuilder();
            foreach (var suffix in new[] { "", ".bak" })
            {
                string path = PathOf(slot) + suffix;
                if (!File.Exists(path)) continue;
                try
                {
                    var root = J.Obj(MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)));
                    int from = J.Int(root, "schema", 0);
                    root = Migrator.Migrate(root);
                    var r = new LoadResult { Family = FromJson(root), UsedBackup = suffix != "", FromSchema = from, Warning = warn.ToString() };
                    var pk = J.Child(root, "packs"); if (pk != null) foreach (var kv in pk) r.PackVersions[kv.Key] = Convert.ToInt32(kv.Value);
                    if (suffix != "") r.Warning += "주 저장 파일이 손상되어 백업에서 복원했습니다.";
                    return r;
                }
                catch (Exception ex) { warn.Append(Path.GetFileName(path) + ": " + ex.Message + " "); }
            }
            throw new InvalidDataException("저장을 불러올 수 없음: " + warn);
        }

        public List<SaveMeta> List()
        {
            var r = new List<SaveMeta>();
            foreach (var s in Slots)
            {
                if (!Exists(s)) continue;
                var m = new SaveMeta { Slot = s };
                try
                {
                    var lr = Load(s);
                    m.FamilyName = lr.Family.Name; m.Day = lr.Family.Today; m.Members = lr.Family.Members.Count; m.Schema = lr.FromSchema;
                }
                catch (Exception) { m.Corrupt = true; }
                r.Add(m);
            }
            return r;
        }
    }

    /// <summary>
    /// 새 게임. ① 원작 시작 가족(로컬 추출 start_family.json)이 있으면 그 이름·생일·능력치·직업·예정 상태를 쓴다.
    /// ② 없으면 임시 가족. 가족 관계(부부·자녀)는 원작 레코드의 관계 필드가 미해명이라 나이·성별로 추정한다.
    /// </summary>
    public static class NewGame
    {
        public static Family Create(ulong seed)
        {
            var rng = new Rng(seed);
            var f = new Family { Name = "다나카 가", Today = GameDate.Make(2005, 1, 1), Mood = 128, Assets = 5000, HouseGrade = 2, RngState = seed };
            Add(f, rng, "타로", 0, 1956, 6, 2000, 2600);
            Add(f, rng, "하나코", 1, 1957, 12, 2100, 2800);
            var son = Add(f, rng, "켄지", 0, 2002, 6, 1500, 1500);
            var dau = Add(f, rng, "유키", 1, 2003, 2, 1200, 1700);
            f.Members[0].SpouseId = f.Members[1].Id; f.Members[1].SpouseId = f.Members[0].Id;
            f.Members[0].Job = 5; f.Members[1].Job = 3;
            son.FatherId = dau.FatherId = 1; son.MotherId = dau.MotherId = 2;
            Finish(f, rng);
            return f;
        }

        public static Family FromOriginal(Dictionary<string, object> sf, ulong seed)
        {
            var rng = new Rng(seed);
            var date = J.List(sf, "date");
            var f = new Family { Name = "가족", Mood = J.Int(sf, "mood", 128), Assets = J.Long(sf, "assets"), HouseGrade = J.Int(sf, "house"), RngState = seed };
            f.Today = date.Count == 3 ? GameDate.Make(Convert.ToInt32(date[0]), Convert.ToInt32(date[1]), Convert.ToInt32(date[2])) : GameDate.Make(2005, 1, 1);
            foreach (var o in J.List(sf, "members"))
            {
                var m = J.Obj(o); var b = J.List(m, "birth");
                var p = new Person { Id = f.NextPersonId++, Name = J.Str(m, "name"), Gender = J.Int(m, "gender"), Job = J.Int(m, "job"),
                    JobMastery = J.Int(m, "mastery"), Immersion = J.Int(m, "interest", 82), Character = J.Str(m, "character"),
                    PlannedStateId = J.Str(m, "plannedStateId"), PlannedTitle = J.Str(m, "plannedTitle"), Hearts = Person.HeartUnit * 3 / 2 };
                if (b.Count == 3) p.BirthDay = GameDate.Make(Convert.ToInt32(b[0]), Convert.ToInt32(b[1]), Convert.ToInt32(b[2]));
                var st = J.List(m, "stats"); for (int i = 0; i < 4 && i < st.Count; i++) p.Stats[i] = Convert.ToInt32(st[i]);
                foreach (var k in J.List(m, "skills")) p.Skills.Add(Convert.ToInt32(k));
                p.Gauge = J.Int(m, "gauge", 136); p.InterestDay = J.Int(m, "interestDay", 1); p.ArrowFlags = J.Int(m, "arrowFlags"); p.PersonalityCode = J.Int(m, "pcode", 0);
                f.Members.Add(p);
            }
            if (f.Members.Count > 0)
            {
                int headSlot = J.Int(sf, "head", 0);
                var head = f.Members[Math.Min(headSlot, f.Members.Count - 1)];
                f.Name = head.Name + " 가";
                // 추정: 세대주와 나이 차 15세 미만인 이성 성인 = 배우자, 세대주보다 18세 이상 어린 사람 = 자녀
                foreach (var p in f.Members)
                {
                    if (p == head) continue;
                    int gap = System.Math.Abs(GameDate.Year(p.BirthDay) - GameDate.Year(head.BirthDay));
                    if (head.SpouseId < 0 && p.Gender != head.Gender && gap < 15 && p.Age(f.Today) >= 18) { head.SpouseId = p.Id; p.SpouseId = head.Id; }
                }
                foreach (var p in f.Members)
                    if (GameDate.Year(p.BirthDay) - GameDate.Year(head.BirthDay) >= 18)
                    {
                        if (head.Gender == 0) { p.FatherId = head.Id; if (head.SpouseId >= 0) p.MotherId = head.SpouseId; }
                        else { p.MotherId = head.Id; if (head.SpouseId >= 0) p.FatherId = head.SpouseId; }
                    }
                f.HeadId = head.Id;
            }
            Finish(f, rng);
            return f;
        }

        static void Finish(Family f, Rng rng)
        {
            f.StartDay = f.Today; if (f.HeadId < 0 && f.Members.Count > 0) f.HeadId = f.Members[0].Id;
            Interventions.GiveStarting(f);
            f.RngState = rng.State;
        }

        static Person Add(Family f, Rng rng, string name, int gender, int y, int m, int baseStat, int charm)
        {
            var p = new Person { Id = f.NextPersonId++, Name = name, Gender = gender, BirthDay = GameDate.Make(y, m, 1 + rng.Next(28)) };
            p.Stats[0] = baseStat + rng.Next(600); p.Stats[1] = baseStat + rng.Next(600); p.Stats[2] = charm + rng.Next(400) - 200; p.Stats[3] = 1500 + rng.Next(1500);
            p.Hearts = Person.HeartUnit * 3 / 2; p.Immersion = 82;
            f.Members.Add(p);
            return p;
        }
    }
}

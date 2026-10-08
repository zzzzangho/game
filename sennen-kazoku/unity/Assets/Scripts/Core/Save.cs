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
        public const int CurrentSchema = 1;
        public static readonly string[] Slots = { "auto", "slot1", "slot2", "slot3" };

        readonly string dir;
        public readonly SaveMigrator Migrator = new SaveMigrator(CurrentSchema);
        public SaveSystem(string dir) { this.dir = dir; Directory.CreateDirectory(dir); }
        public SaveSystem(string dir, SaveMigrator m) { this.dir = dir; Directory.CreateDirectory(dir); Migrator = m; }

        string PathOf(string slot) { return Path.Combine(dir, "save_" + slot + ".json"); }

        // ---------- 직렬화 ----------
        public static Dictionary<string, object> ToJson(Family f, ContentCatalog cat, int schema)
        {
            var members = new List<object>(); foreach (var p in f.Members) members.Add(p.ToJson());
            var hist = new List<object>();
            foreach (var h in f.History)
                hist.Add(new Dictionary<string, object> { { "day", h.Day }, { "id", h.EventId }, { "ver", h.EventVersion }, { "title", h.Title }, { "person", h.PersonId }, { "choice", h.Choice } });
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
                    { "active", f.Active != null ? f.Active.ToJson() : null }, { "queue", queue } } }
            };
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
                f.History.Add(new EventRecord { Day = J.Int(h, "day"), EventId = J.Str(h, "id"), EventVersion = J.Int(h, "ver"), Title = J.Str(h, "title"), PersonId = J.Int(h, "person", -1), Choice = J.Str(h, "choice") });
            }
            var lf = J.Child(d, "lastFired"); if (lf != null) foreach (var kv in lf) f.LastFired[kv.Key] = Convert.ToInt32(kv.Value);
            var fc = J.Child(d, "fireCount"); if (fc != null) foreach (var kv in fc) f.FireCount[kv.Key] = Convert.ToInt32(kv.Value);
            ulong rng; if (ulong.TryParse(J.Str(d, "rng"), out rng)) f.RngState = rng;
            var act = J.Child(d, "active"); if (act != null) f.Active = ActiveEvent.FromJson(act);
            foreach (var o in J.List(d, "queue")) f.Queue.Add(ActiveEvent.FromJson(J.Obj(o)));
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

    /// <summary>새 게임 시작 가족. 이름·능력치는 임시 값이다(원작 시작 가족 데이터는 미확보).</summary>
    public static class NewGame
    {
        public static Family Create(ulong seed)
        {
            var rng = new Rng(seed);
            var f = new Family { Name = "다나카 가", Today = GameDate.Make(1985, 4, 1), Mood = 128, Assets = 300000, HouseGrade = 1, RngState = seed };
            Add(f, rng, "타로", 0, 1955, 3, 2000, 2600);
            Add(f, rng, "하나코", 1, 1958, 8, 2100, 2800);
            f.Members[0].SpouseId = f.Members[1].Id; f.Members[1].SpouseId = f.Members[0].Id;
            f.Members[0].Job = 5; f.Members[1].Job = 3;
            var son = Add(f, rng, "켄지", 0, 1982, 6, 1500, 1500); son.FatherId = 1; son.MotherId = 2;
            var dau = Add(f, rng, "유키", 1, 1984, 2, 1200, 1700); dau.FatherId = 1; dau.MotherId = 2;
            f.RngState = rng.State;
            return f;
        }

        static Person Add(Family f, Rng rng, string name, int gender, int y, int m, int baseStat, int charm)
        {
            var p = new Person { Id = f.NextPersonId++, Name = name, Gender = gender, BirthDay = GameDate.Make(y, m, 1 + rng.Next(28)) };
            p.Stats[0] = baseStat + rng.Next(600); p.Stats[1] = baseStat + rng.Next(600); p.Stats[2] = charm + rng.Next(400) - 200; p.Stats[3] = 1500 + rng.Next(1500);
            p.Hearts = 300; p.Immersion = 82;
            f.Members.Add(p);
            return p;
        }
    }
}

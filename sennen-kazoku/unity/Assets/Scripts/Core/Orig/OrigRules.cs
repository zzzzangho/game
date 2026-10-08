using System;
using System.Collections.Generic;

namespace SennenKazoku.Core.Orig
{
    /// <summary>원작 관심사 항목 (ROM 0x085BD4A0[표][번호] 의 0x28 바이트 중 쓰는 값).</summary>
    public sealed class OrigInterest
    {
        public int Table, Index, Type, Gender, GaugeStart;
        public string Pred = "";
    }

    /// <summary>
    /// tools/romlift/export_orig.py 가 사용자 ROM 에서 뽑은 원작 규칙 데이터 (로컬 팩 전용).
    /// 판정 트리·관심사 항목·후보 목록·선택 표·직업 후보표·특별 후보표·ROM 표 조각.
    /// </summary>
    public sealed class OrigRules
    {
        public readonly Dictionary<long, OrigInterest> Interests = new Dictionary<long, OrigInterest>();
        public readonly Dictionary<int, List<int>[,]> Candidates = new Dictionary<int, List<int>[,]>();   // 유형 → [단계, 시대]
        public int[,] ModeTable = new int[8, 16];
        public int[] B15Table = new int[3];
        public readonly List<OrigJob> Jobs = new List<OrigJob>();
        public readonly List<List<int[]>> Specials = new List<List<int[]>>();   // [확률, 표, 번호]
        public object TreesJson;           // 실행기에 넣을 트리 (지연 생성)
        public List<object> RomJson;

        public sealed class OrigJob
        {
            public List<int> List = new List<int>();
            public List<List<int>> Ranks = new List<List<int>>();
            public List<int> Map = new List<int>();
        }

        public static long Key(int table, int index) { return ((long)table << 16) | (uint)index; }
        public OrigInterest Get(int table, int index) { return Interests.TryGetValue(Key(table, index), out var x) ? x : null; }

        static List<int> Ints(object o)
        {
            var r = new List<int>();
            if (o is List<object> l) foreach (var v in l) r.Add(Convert.ToInt32(v));
            return r;
        }

        public static OrigRules FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var r = new OrigRules();
            foreach (var o in J.List(d, "interests"))
            {
                var l = (List<object>)o;
                var it = new OrigInterest { Table = Convert.ToInt32(l[0]), Index = Convert.ToInt32(l[1]), Type = Convert.ToInt32(l[2]),
                    Gender = Convert.ToInt32(l[3]), GaugeStart = Convert.ToInt32(l[4]), Pred = (string)l[5] };
                r.Interests[Key(it.Table, it.Index)] = it;
            }
            var c = J.Child(d, "candidates");
            if (c != null)
                foreach (var kv in c)
                {
                    var arr = new List<int>[8, 4];
                    var st = (List<object>)kv.Value;
                    for (int s = 0; s < 8 && s < st.Count; s++)
                    {
                        var eras = (List<object>)st[s];
                        for (int e = 0; e < 4 && e < eras.Count; e++) arr[s, e] = Ints(eras[e]);
                    }
                    r.Candidates[int.Parse(kv.Key)] = arr;
                }
            var mt = J.List(d, "modeTable");
            for (int s = 0; s < 8 && s < mt.Count; s++) { var row = Ints(mt[s]); for (int k = 0; k < 16 && k < row.Count; k++) r.ModeTable[s, k] = row[k]; }
            var bt = Ints(d.TryGetValue("b15Table", out var bo) ? bo : null);
            for (int k = 0; k < 3 && k < bt.Count; k++) r.B15Table[k] = bt[k];
            foreach (var o in J.List(d, "jobs"))
            {
                var jd = (Dictionary<string, object>)o; var job = new OrigJob { List = Ints(jd["list"]), Map = Ints(jd["map"]) };
                foreach (var rk in (List<object>)jd["ranks"]) job.Ranks.Add(Ints(rk));
                r.Jobs.Add(job);
            }
            foreach (var o in J.List(d, "specials"))
            {
                var set = new List<int[]>();
                foreach (var e in (List<object>)o) set.Add(Ints(e).ToArray());
                r.Specials.Add(set);
            }
            r.TreesJson = d.TryGetValue("trees", out var tj) ? tj : null;
            r.RomJson = J.List(d, "rom");
            return r;
        }

        /// <summary>ROM 표 조각을 메모리에 넣고, 판정 트리를 실을 실행기를 만든다.</summary>
        public OrigVm CreateVm(OrigMem mem)
        {
            if (!mem.HasRom && RomJson != null)
                foreach (var o in RomJson)
                {
                    var l = (List<object>)o;
                    mem.AddRom((uint)Convert.ToInt64(l[0]), Hex((string)l[1]));
                }
            var vm = new OrigVm(mem);
            if (TreesJson is Dictionary<string, object> t)
                foreach (var kv in t) vm.AddTree(kv.Key, kv.Value);
            return vm;
        }

        static byte[] Hex(string s)
        {
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(2 * i, 2), 16);
            return b;
        }
    }
}

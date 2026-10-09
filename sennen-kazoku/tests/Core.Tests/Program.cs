using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SennenKazoku.Core;

namespace SennenKazoku.Tests
{
    static class T
    {
        public static int Pass, Fail;
        public static void Run(string name, Action a)
        {
            try { a(); Pass++; Console.WriteLine("  ok   " + name); }
            catch (Exception e) { Fail++; Console.WriteLine("  FAIL " + name + "\n       " + e.Message.Replace("\n", "\n       ")); if (Environment.GetEnvironmentVariable("SK_TRACE") != null) Console.WriteLine(e.StackTrace); }
        }
        public static void True(bool c, string m = "assert") { if (!c) throw new Exception(m); }
        public static void Eq<X>(X a, X b, string m = "") { if (!EqualityComparer<X>.Default.Equals(a, b)) throw new Exception(m + " 기대=" + b + " 실제=" + a); }
    }

    static class Program
    {
        static string PacksDir;
        static string Tmp() { var d = Path.Combine(Path.GetTempPath(), "sk_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
        static string Read(string name)
        {
            // nova.pack001 은 게임에 넣지 않는 확장 구조 시험용 팩(선택지 이벤트 — 원작에는 이런 이벤트가 없다)
            var fx = Path.Combine(AppContext.BaseDirectory, "../../../Fixtures", name + ".json");
            return File.ReadAllText(File.Exists(fx) ? fx : Path.Combine(PacksDir, name + ".json"));
        }

        static Pack LoadPack(string json)
        {
            var e = new List<string>(); var p = Pack.Load(json, e);
            if (p == null) throw new Exception("팩 로드 실패: " + string.Join("; ", e));
            return p;
        }
        /// <summary>tools/romlift/make_vectors.py 가 원작 ROM 을 실제 RAM 덤프 위에서 돌린 결과와 C# 이식을 비교한다.</summary>
        static void OrigVectors(string dir)
        {
            SennenKazoku.Core.Orig.OrigVm.Trail = Environment.GetEnvironmentVariable("SK_TRACE") != null;
            var rules = SennenKazoku.Core.Orig.OrigRules.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "orig_rules.json")))));
            if (rules.TreesFile != "" && File.Exists(Path.Combine(dir, rules.TreesFile))) rules.TreesText = File.ReadAllBytes(Path.Combine(dir, rules.TreesFile));
            var vec = J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "vectors.json"))));
            var shared = new SennenKazoku.Core.Orig.OrigMem();
            var vm0 = rules.CreateVm(shared);
            var rams = new Dictionary<string, byte[]>();
            byte[] Ram(string n) { if (!rams.TryGetValue(n, out var b)) rams[n] = b = File.ReadAllBytes(Path.Combine(dir, n)); return b; }
            SennenKazoku.Core.Orig.OrigMem Fresh(string n)
            {
                var m = new SennenKazoku.Core.Orig.OrigMem(); m.ShareRom(shared);
                Array.Copy(Ram(n), m.Ewram, SennenKazoku.Core.Orig.OrigMem.EwramSize); return m;
            }
            int same = 0, total = 0, unm = 0;
            foreach (var o in J.List(vec, "preds"))
            {
                var d = J.Obj(o); var mem = Fresh(J.Str(d, "ram"));
                SennenKazoku.Core.Orig.OrigFamily.BuildSlots(mem, (uint)J.Int(d, "id"));
                var vm = rules.CreateVm(mem);
                var centered = (byte[])mem.Ewram.Clone();
                var preds = J.List(d, "preds"); var exp = J.List(d, "expect");
                for (int i = 0; i < preds.Count; i++)
                {
                    if (exp[i] == null) continue;
                    Array.Copy(centered, mem.Ewram, centered.Length);
                    total++;
                    try { if (vm.Call((string)preds[i]) == (uint)Convert.ToInt64(exp[i])) same++; }
                    catch (SennenKazoku.Core.Orig.OrigUnmodeled) { unm++; }
                }
            }
            Console.WriteLine("       판정 함수 " + total + "건 중 원작과 같음 " + same + " (옮기지 못함 " + unm + ")");
            T.Eq(same, total, "판정 함수 결과가 원작과 다름");
            int sOk = 0, sN = 0;
            foreach (var o in J.List(vec, "select"))
            {
                var d = J.Obj(o); if (d.ContainsKey("error")) continue;
                var mem = Fresh(J.Str(d, "ram")); var vm = rules.CreateVm(mem);
                int n = J.Int(d, "person"); uint p = SennenKazoku.Core.Orig.OrigMem.PersonAddr(n);
                SennenKazoku.Core.Orig.OrigFamily.BuildSlots(mem, mem.R16(p + 0x3C));
                mem.W32(SennenKazoku.Core.Orig.OrigMem.Seed, (uint)J.Long(d, "seed"));
                var before = (byte[])mem.Ewram.Clone();
                new SennenKazoku.Core.Orig.OrigSelect(mem, vm, rules).Select(p, (uint)J.Int(d, "era"), (uint)J.Int(d, "stage"));
                var want = new Dictionary<int, byte>();
                foreach (var w in J.List(d, "writes")) { var l = (List<object>)w; want[(int)(Convert.ToInt64(l[0]) - 0x02000000)] = (byte)Convert.ToInt32(l[1]); }
                bool ok = true;
                for (int i = 0; i < before.Length && ok; i++)
                {
                    byte expect = want.TryGetValue(i, out var b) ? b : before[i];
                    if (mem.Ewram[i] != expect) { ok = false; Console.WriteLine("       선택 불일치 " + J.Str(d, "ram") + " 인물" + n + " seed " + J.Long(d, "seed") + " @" + (0x02000000 + i).ToString("X8")); }
                }
                sN++; if (ok) sOk++;
            }
            Console.WriteLine("       관심사 선택 " + sN + "건 중 원작과 메모리 변화가 같음 " + sOk);
            T.Eq(sOk, sN, "관심사 선택 결과가 원작과 다름");
            int eOk = 0, eN = 0, eUnm = 0;
            foreach (var o in J.List(vec, "events"))
            {
                var d = J.Obj(o); if (d.ContainsKey("error")) continue;
                var mem = Fresh(J.Str(d, "ram")); var vm = rules.CreateVm(mem);
                int n = J.Int(d, "person"); uint p = SennenKazoku.Core.Orig.OrigMem.PersonAddr(n);
                SennenKazoku.Core.Orig.OrigFamily.BuildSlots(mem, mem.R16(p + 0x3C));
                mem.W32(SennenKazoku.Core.Orig.OrigMem.Seed, (uint)J.Long(d, "seed"));
                var before = (byte[])mem.Ewram.Clone();
                eN++;
                uint data;
                try
                {
                    uint ev = SennenKazoku.Core.Orig.OrigEvents.EventOf(mem, n, J.Bool(d, "max"));
                    int k = SennenKazoku.Core.Orig.OrigEvents.PickVariant(vm, rules, ev);
                    data = rules.Events[ev].Data[k];
                    SennenKazoku.Core.Orig.OrigEvents.RunEffect(vm, rules, data);
                }
                catch (SennenKazoku.Core.Orig.OrigUnmodeled ex) { eUnm++; Console.WriteLine("       사건 옮기지 못함: " + ex.Message); continue; }
                if (data != (uint)J.Long(d, "data")) { Console.WriteLine("       변형이 다름 " + data.ToString("X8") + " / " + J.Long(d, "data").ToString("X8")); continue; }
                var want = new Dictionary<int, byte>();
                foreach (var w in J.List(d, "writes")) { var l = (List<object>)w; want[(int)(Convert.ToInt64(l[0]) - 0x02000000)] = (byte)Convert.ToInt32(l[1]); }
                bool ok = true;
                for (int i = 0; i < before.Length && ok; i++)
                {
                    byte expect = want.TryGetValue(i, out var b) ? b : before[i];
                    if (mem.Ewram[i] != expect) { ok = false; Console.WriteLine("       사건 효과 불일치 @" + (0x02000000 + i).ToString("X8") + " 결과기록 " + data.ToString("X8")); }
                }
                if (ok) eOk++;
            }
            Console.WriteLine("       MAX/MIN 사건(변형 고르기+효과) " + eN + "건 중 원작과 같음 " + eOk + " (옮기지 못함 " + eUnm + ")");
            T.Eq(eOk, eN, "사건 결과가 원작과 다름");

            // 원작 코드로 60일 진행 (관심사 하루 처리 → 사건 → 다음 관심사). 예외 없이 돌고 사건이 나야 한다.
            {
                var mem = Fresh("main.ram");
                var game = new SennenKazoku.Core.Orig.OrigGame(mem, rules);
                var sw = System.Diagnostics.Stopwatch.StartNew(); int nev = 0, days = 60;
                var titles = new List<string>();
                for (int dday = 0; dday < days; dday++)
                {
                    foreach (var ev in game.TickDay())
                    {
                        nev++;
                        uint p = SennenKazoku.Core.Orig.OrigMem.PersonAddr(ev.Person);
                        titles.Add("일" + dday + " 인물" + ev.Person + " 큐종류" + ev.Type + " 코드" + ev.Code + " 결과 " + ev.Data.ToString("X8") + " → 관심사 " + mem.R16(p + 0x80) + "," + mem.R16(p + 0x82) + " 게이지 " + mem.R8(p + 0x5A));
                    }
                    game.NextDate();
                }
                SennenKazoku.Core.Orig.OrigDate.Get(mem, SennenKazoku.Core.Orig.OrigMem.Date, out int yy, out int mm, out int dd);
                Console.WriteLine("       원작 코드로 " + days + "일 진행: 사건 " + nev + "번, " + sw.ElapsedMilliseconds + "ms, 날짜 " + yy + "-" + mm + "-" + dd);
                foreach (var tl in titles.Take(10)) Console.WriteLine("         " + tl);
                T.True(nev > 0, "60일 동안 사건이 없음");
            }

            // 새 가족 만들기: 원작 실행 중단점 덤프(newg_*.ram)에서 같은 단계를 변환 트리로 돌려 원작 결과와 비교
            if (File.Exists(Path.Combine(dir, "newg_fin0.ram")) && rules.CreateVm(shared).HasTree("0802D7A0"))
            {
                const uint E = SennenKazoku.Core.Orig.OrigMem.EwramBase;
                SennenKazoku.Core.Orig.OrigMem Full(string n)
                {
                    var m = Fresh(n); Array.Copy(Ram(n), (int)SennenKazoku.Core.Orig.OrigMem.EwramSize, m.Iwram, 0, m.Iwram.Length); return m;
                }
                int Diff(SennenKazoku.Core.Orig.OrigMem m, byte[] want, uint lo, uint hi, uint skipLo = 0, uint skipHi = 0)
                {
                    int n = 0;
                    for (uint a = lo; a < hi; a++)
                        if ((a < skipLo || a >= skipHi) && m.Ewram[a - E] != want[a - E]) { if (n < 5) Console.WriteLine("         다름 @" + a.ToString("X8") + " " + m.Ewram[a - E] + " / " + want[a - E]); n++; }
                    return n;
                }
                const uint S = SennenKazoku.Core.Orig.OrigNewGame.Setup, SEnd = S + 0x44 + 8 * 0x9F0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var mf = Full("newg_fin0.ram"); var vmf = rules.CreateVm(mf);
                mf.W32(SennenKazoku.Core.Orig.OrigNewGame.SceneObj, S); mf.W8(SennenKazoku.Core.Orig.OrigNewGame.SceneObj + 0x11, 0);
                vmf.Call("0802D7A0", SennenKazoku.Core.Orig.OrigNewGame.SceneObj);
                var w1 = Ram("newg_fin1.ram");
                int d1 = Diff(mf, w1, S, SEnd) + Diff(mf, w1, SennenKazoku.Core.Orig.OrigMem.SaveStart, SennenKazoku.Core.Orig.OrigMem.SaveEnd);
                Console.WriteLine("       구성원 마무리(0x0802D7A0): 설정 블록·저장 영역 원작과 다른 바이트 " + d1 + " (" + sw.ElapsedMilliseconds + "ms)");
                T.Eq(d1, 0, "구성원 마무리 결과가 원작과 다름");
                sw.Restart();
                var mc = Full("newg_pre.ram"); var vmc = rules.CreateVm(mc);
                vmc.Call("080417E0", S);
                // 가족 머리말 0x0202C6A0~C3 의 성 문자열 뒤·패딩 칸은 원작 스택 찌꺼기, 0x0203BE04 는 다음 장면 작업 핸들
                // (장면 바꾸기 0x0809AF60 은 실행하지 않음)이라 비교에서 뺀다
                var wc = Ram("newg_create.ram");
                int d2 = Diff(mc, wc, SennenKazoku.Core.Orig.OrigMem.SaveStart, 0x0203BE04, 0x0202C6A0, 0x0202C6C4)
                       + Diff(mc, wc, 0x0203BE08, SennenKazoku.Core.Orig.OrigMem.SaveEnd);
                Console.WriteLine("       가족 레코드 만들기(0x080417E0): 저장 영역 원작과 다른 바이트 " + d2 + " (머리말 36바이트·장면 핸들 4바이트 제외, " + sw.ElapsedMilliseconds + "ms)");
                T.Eq(d2, 0, "가족 레코드 만들기 결과가 원작과 다름");
                // 빈 메모리에서 원작처럼 저장 영역 초기화(전원 켤 때 0, 제목 화면 새로 시작) → 새 가족 장면 직전 원작 저장 영역과 비교
                var mn = new SennenKazoku.Core.Orig.OrigMem(); mn.ShareRom(shared); var vmn = rules.CreateVm(mn);
                SennenKazoku.Core.Orig.OrigNewGame.BlankCartridge(vmn);
                SennenKazoku.Core.Orig.OrigNewGame.TitleNewGame(vmn);
                {
                    // 비교에서 빼는 칸: 시계 날짜(0x0202C688, 뒤에서 적음)·성(0x0202C6A0, 추천 가족 틀에서 적음)·
                    // 화면 작업 변수(0x0203BD68, 가족 고르기 화면이 여러 번 바꿈)·장면 작업 핸들(0x0203BE04)
                    bool Skip(uint a) { return (a >= 0x0202C688 && a < 0x0202C68B) || (a >= 0x0202C6A0 && a < 0x0202C6C4) || a == 0x0203BD68 || (a >= 0x0203BE04 && a < 0x0203BE08); }
                    var w0 = Ram("newg_fin0.ram"); var runs = new List<string>(); int nd = 0;
                    for (uint a = SennenKazoku.Core.Orig.OrigMem.SaveStart; a < SennenKazoku.Core.Orig.OrigMem.SaveEnd; a++)
                    {
                        if (mn.Ewram[a - E] == w0[a - E] || Skip(a)) continue;
                        nd++;
                        if (runs.Count > 0 && runs[runs.Count - 1].EndsWith((a - 1).ToString("X8"))) runs[runs.Count - 1] = runs[runs.Count - 1].Substring(0, 9) + a.ToString("X8");
                        else runs.Add(a.ToString("X8") + "-" + a.ToString("X8"));
                    }
                    Console.WriteLine("       빈 카트리지 → 제목 화면 새로 시작: 저장 영역 원작(새 가족 장면 직전)과 다른 바이트 " + nd + (nd > 0 ? ": " + string.Join(" ", runs.Take(30)) : "") + " (날짜·성·화면 변수·장면 핸들 제외)");
                    T.Eq(nd, 0, "저장 영역 초기화가 원작과 다름");
                }
                SennenKazoku.Core.Orig.OrigNewGame.Recommended(vmn);
                int people = 0; for (int k = 0; k < 8; k++) if (SennenKazoku.Core.Orig.OrigGame.Present(mn, k)) people++;
                SennenKazoku.Core.Orig.OrigDate.Get(mn, SennenKazoku.Core.Orig.OrigMem.Date, out int ny, out int nm2, out int nd2);
                Console.WriteLine("       빈 카트리지에서 추천 가족 새로 만들기: " + people + "명, 날짜 " + ny + "-" + nm2 + "-" + nd2);
                T.True(people >= 2, "새 가족 인원이 너무 적음");
                // 만든 가족을 원작 하루 처리로 60일 진행
                var g2 = new SennenKazoku.Core.Orig.OrigGame(mn, rules); int ev2 = 0;
                for (int dday = 0; dday < 60; dday++) { ev2 += g2.TickDay().Count; g2.NextDate(); }
                Console.WriteLine("       새 가족 60일 진행: 사건 " + ev2 + "번");
            }
        }

        static void OrigDays(string dir, string daysDir)
        {
            var rules = SennenKazoku.Core.Orig.OrigRules.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "orig_rules.json")))));
            rules.TreesText = File.ReadAllBytes(Path.Combine(dir, rules.TreesFile));
            var shared = new SennenKazoku.Core.Orig.OrigMem(); rules.CreateVm(shared);
            var files = Directory.GetFiles(daysDir, "*.ram").OrderBy(f => f).ToList();
            const uint E = SennenKazoku.Core.Orig.OrigMem.EwramBase;
            int okDays = 0;
            for (int i = 0; i + 1 < files.Count; i++)
            {
                var a = File.ReadAllBytes(files[i]); var b = File.ReadAllBytes(files[i + 1]);
                var m = new SennenKazoku.Core.Orig.OrigMem(); m.ShareRom(shared);
                Array.Copy(a, m.Ewram, SennenKazoku.Core.Orig.OrigMem.EwramSize);
                Array.Copy(a, (int)SennenKazoku.Core.Orig.OrigMem.EwramSize, m.Iwram, 0, m.Iwram.Length);
                var g = new SennenKazoku.Core.Orig.OrigGame(m, rules);
                g.NextDate();
                string evs;
                try { evs = string.Join(",", g.TickDay().Select(x => x.Person + ":" + x.Type + ":" + x.Data.ToString("X"))); }
                catch (SennenKazoku.Core.Orig.OrigUnmodeled ex) { Console.WriteLine("       " + Path.GetFileName(files[i]) + " 옮기지 못함: " + ex.Message); continue; }
                var runs = new List<string>(); int nd = 0;
                for (uint x = SennenKazoku.Core.Orig.OrigMem.SaveStart; x < SennenKazoku.Core.Orig.OrigMem.SaveEnd; x++)
                {
                    if (m.Ewram[x - E] == b[x - E]) continue;
                    nd++;
                    if (runs.Count > 0 && runs[runs.Count - 1].EndsWith((x - 1).ToString("X8"))) runs[runs.Count - 1] = runs[runs.Count - 1].Substring(0, 9) + x.ToString("X8");
                    else runs.Add(x.ToString("X8") + "-" + x.ToString("X8"));
                }
                if (nd == 0) okDays++;
                if (Environment.GetEnvironmentVariable("SK_DAYS_DUMP") == Path.GetFileName(files[i]))
                    for (uint x = 0x0202C6C4; x < 0x0203BD60; x++)
                        if (m.Ewram[x - E] != b[x - E] && (x < 0x0203BC90 || x >= 0x0203BD00))
                            Console.WriteLine("           " + x.ToString("X8") + " C# " + m.Ewram[x - E].ToString("X2") + " 원작 " + b[x - E].ToString("X2") + " 시작 " + a[x - E].ToString("X2"));
                Console.WriteLine("         게이지 C# " + string.Join(",", Enumerable.Range(0, 4).Select(k => m.R8(SennenKazoku.Core.Orig.OrigMem.PersonAddr(k) + 0x5A))) + " / 원작 " + string.Join(",", Enumerable.Range(0, 4).Select(k => b[SennenKazoku.Core.Orig.OrigMem.PersonAddr(k) + 0x5A - E])) + "  시작 " + string.Join(",", Enumerable.Range(0, 4).Select(k => a[SennenKazoku.Core.Orig.OrigMem.PersonAddr(k) + 0x5A - E])));
                Console.WriteLine("       " + Path.GetFileName(files[i]) + " 사건[" + evs + "] 다른 바이트 " + nd + (nd > 0 ? ": " + string.Join(" ", runs.Take(60)) : ""));
            }
            Console.WriteLine("       " + (files.Count - 1) + "일 중 저장 영역이 원작과 완전히 같은 날 " + okDays);
            // 사건 장면 덤프(시작 0804c84c → 효과 직전 0804d9b4 → 복귀 뒤 0804df40): 효과 직전 상태에서 C# 효과+복귀 처리 → 복귀 뒤와 비교
            var sdir = Environment.GetEnvironmentVariable("SK_SCENES_DIR");
            if (!string.IsNullOrEmpty(sdir))
            {
                var sf = Directory.GetFiles(sdir, "*.ram").OrderBy(f => f).ToList();
                var slog = File.ReadAllLines(Path.Combine(sdir, "..", "rams", "sc.log"));
                SennenKazoku.Core.Orig.OrigMem Load(string f)
                {
                    var a = File.ReadAllBytes(f); var m = new SennenKazoku.Core.Orig.OrigMem(); m.ShareRom(shared);
                    Array.Copy(a, m.Ewram, SennenKazoku.Core.Orig.OrigMem.EwramSize);
                    Array.Copy(a, (int)SennenKazoku.Core.Orig.OrigMem.EwramSize, m.Iwram, 0, m.Iwram.Length); return m;
                }
                int sameScenes = 0, nScenes = 0;
                for (int i = 0; i + 2 < sf.Count && i + 2 < slog.Length; i++)
                {
                    if (!slog[i].Contains("pc=0804c84c") || !slog[i + 1].Contains("pc=0804d9b4") || !slog[i + 2].Contains("pc=0804df40")) continue;
                    uint data = Convert.ToUInt32(slog[i].Split(' ').First(x => x.StartsWith("r1=")).Substring(3), 16);
                    var mp = Load(sf[i]); var vmp = rules.CreateVm(mp);
                    uint v = rules.VariantData.TryGetValue(data, out var fns) && fns[0] != null ? vmp.Call(fns[0], 0, data, 0, 0) & 0xFFFF : 0;
                    var me = Load(sf[i + 1]); var vme = rules.CreateVm(me);
                    uint rd = me.R16(0x0203BBCA), qe = 0x0203BBD0 + 8 * ((rd + 23) % 24), id = me.R16(qe + 6);
                    SennenKazoku.Core.Orig.OrigEvents.RunEffect(vme, rules, data, v);
                    vme.Call("080111B8", 0xFFFFFFFD, id, 1, 0, v, 0, data);
                    var want = File.ReadAllBytes(sf[i + 2]);
                    var diffs = new List<string>(); int nd = 0;
                    for (uint x = 0x0202C6C4; x < SennenKazoku.Core.Orig.OrigMem.SaveEnd; x++)
                        if (me.Ewram[x - E] != want[x - E] && (x < 0x0203BC90 || x >= 0x0203BCB0) && x != 0x0203BCF0 && x != 0x0203BCF1 && x != 0x0203BAD0)
                        { nd++; if (diffs.Count < 12) diffs.Add(x.ToString("X8") + ":" + me.Ewram[x - E].ToString("X2") + "/" + want[x - E].ToString("X2")); }
                    nScenes++; if (nd == 0) sameScenes++;
                    Console.WriteLine("       사건 장면 " + Path.GetFileName(sf[i]) + " 결과 " + data.ToString("X8") + " 인물 " + id + " 시작값 " + v + ": 다른 바이트 " + nd + " " + string.Join(" ", diffs));
                }
                Console.WriteLine("       사건 장면(효과+복귀) " + nScenes + "건 중 원작과 같음 " + sameScenes + " (작업 위치표 0x0203BC90~AF·0x0203BCF0·장면 핸들 0x0203BAD0 제외)");
            }
            // 메인 장면 변형 고르기 지점 덤프(p_NN_080187b2: 슬롯표 전, 다음 덤프 080187d4: 고른 뒤)에서 C# 슬롯표+변형 고르기 비교
            var pdir = Environment.GetEnvironmentVariable("SK_PICKS_DIR");
            if (!string.IsNullOrEmpty(pdir))
            {
                var pf = Directory.GetFiles(pdir, "*_080187b2.ram").OrderBy(f => f).ToList();
                foreach (var f in pf)
                {
                    var a = File.ReadAllBytes(f);
                    var m = new SennenKazoku.Core.Orig.OrigMem(); m.ShareRom(shared);
                    Array.Copy(a, m.Ewram, SennenKazoku.Core.Orig.OrigMem.EwramSize);
                    Array.Copy(a, (int)SennenKazoku.Core.Orig.OrigMem.EwramSize, m.Iwram, 0, m.Iwram.Length);
                    var vm = rules.CreateVm(m);
                    var line = File.ReadAllLines(Path.Combine(pdir, "..", "rams", "pk.log")).Where(l => l.Contains("pc=080187b2")).ElementAt(pf.IndexOf(f));
                    uint id = Convert.ToUInt32(line.Split(' ').First(x => x.StartsWith("r0=")).Substring(3), 16);
                    uint ev = Convert.ToUInt32(line.Split(' ').First(x => x.StartsWith("r1=")).Substring(3), 16);
                    var nxt = File.ReadAllLines(Path.Combine(pdir, "..", "rams", "pk.log")).Where(l => l.Contains("pc=080187d4")).ElementAt(pf.IndexOf(f));
                    uint want = Convert.ToUInt32(nxt.Split(' ').First(x => x.StartsWith("r0=")).Substring(3), 16);
                    vm.Call("slots", id, ev);
                    int k = SennenKazoku.Core.Orig.OrigEvents.PickVariant(vm, rules, ev);
                    uint got = rules.Events[ev].Data[k];
                    Console.WriteLine("       변형 고르기 " + Path.GetFileName(f) + " 인물 " + id + " 사건 " + ev.ToString("X8") + ": C# " + got.ToString("X8") + " 원작 " + want.ToString("X8") + (got == want ? " 같음" : " 다름"));
                }
            }
        }

        static List<Pack> Bundled() { return new List<Pack> { LoadPack(Read("sk.sample")), LoadPack(Read("nova.pack001")) }; }
        static ContentCatalog Cat(List<Pack> b) { var e = new List<string>(); var c = ContentCatalog.Build(b, e); if (c == null) throw new Exception(string.Join("; ", e)); return c; }

        static int Main(string[] args)
        {
            PacksDir = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../unity/Assets/Resources/BundledPacks"));
            if (!Directory.Exists(PacksDir)) { Console.WriteLine("팩 디렉터리 없음: " + PacksDir); return 2; }
            Console.WriteLine("팩 디렉터리: " + PacksDir);

            Console.WriteLine("[JSON]");
            T.Run("라운드트립(한글·이스케이프·숫자)", () => {
                var o = new Dictionary<string, object> { { "a", "가나다\"\\\n\u0001" }, { "n", -12L }, { "d", 1.5 }, { "l", new List<object> { 1L, true, null } } };
                var back = J.Obj(MiniJson.Parse(MiniJson.Serialize(o, true)));
                T.Eq(J.Str(back, "a"), "가나다\"\\\n\u0001"); T.Eq(J.Long(back, "n"), -12L);
                T.Eq(J.List(back, "l").Count, 3);
            });
            T.Run("잘못된 JSON 거부", () => { foreach (var s in new[] { "{", "{\"a\":}", "[1,,2]", "{\"a\":1} x", "\"abc" }) { bool threw = false; try { MiniJson.Parse(s); } catch (FormatException) { threw = true; } T.True(threw, s); } });

            Console.WriteLine("[팩 검증]");
            T.Run("번들 팩 로드·교차 검증", () => { var c = Cat(Bundled()); T.True(c.Events.Count >= 7); T.Eq(c.States.Count, 3); });
            T.Run("미지원 조건 op → 앱 업데이트 필요 오류", () => {
                var j = Read("nova.pack001").Replace("\"op\": \"chance\"", "\"op\": \"chance\"").Replace("\"op\": \"and\"", "\"op\": \"teleport\"");
                var e = new List<string>(); T.True(Pack.Load(j, e) == null); T.True(e.Any(x => x.Contains("앱 업데이트")), string.Join("|", e));
            });
            T.Run("미지원 효과 op / 알 수 없는 변수 / 잘못된 역할", () => {
                var j = Read("nova.pack001").Replace("\"family_mood\"", "\"summon_ghost\"").Replace("self.age", "self.wealth");
                var e = new List<string>(); T.True(Pack.Load(j, e) == null);
                T.True(e.Any(x => x.Contains("summon_ghost"))); T.True(e.Any(x => x.Contains("self.wealth")));
            });
            T.Run("계약 버전이 높은 팩 거부", () => {
                var e = new List<string>(); T.True(Pack.Load(Read("nova.pack001").Replace("\"minContract\": 1", "\"minContract\": 99"), e) == null);
                T.True(e.Any(x => x.Contains("계약")));
            });
            T.Run("이벤트 id 충돌·의존 누락 거부", () => {
                var a = LoadPack(Read("sk.sample"));
                var dup = LoadPack(Read("nova.pack001").Replace("nova.picnic.001", "sk.s1.good").Replace("nova.pack001", "nova.dup"));
                var e = new List<string>(); T.True(ContentCatalog.Build(new List<Pack> { a, dup }, e) == null); T.True(e.Any(x => x.Contains("충돌")));
                var e2 = new List<string>(); T.True(ContentCatalog.Build(new List<Pack> { LoadPack(Read("nova.pack001")) }, e2) == null); T.True(e2.Any(x => x.Contains("필요")));
            });

            Console.WriteLine("[규칙]");
            T.Run("조사 처리", () => {
                var f = new Family(); f.Members.Add(new Person { Id = 1, Name = "켄지" }); f.Members.Add(new Person { Id = 2, Name = "하나코" });
                var c = new Dictionary<string, int> { { "self", 1 }, { "spouse", 2 } };
                T.Eq(Template.Render("{self:은는}/{self:이가}/{spouse:은는}/{spouse:이가}/{spouse:으로}", f, c), "켄지는/켄지가/하나코는/하나코가/하나코로");
                f.Members.Add(new Person { Id = 3, Name = "민준" });
                T.Eq(Template.Render("{x:은는}", f, c), "{x:은는}");
                T.Eq(Template.Josa("민준", "은는"), "은"); T.Eq(Template.Josa("서울", "으로"), "로");
            });
            T.Run("능력치 등급 경계(확인된 규칙)", () => { T.Eq(Stat.Rank(0), "F"); T.Eq(Stat.Rank(799), "F"); T.Eq(Stat.Rank(800), "D"); T.Eq(Stat.Rank(4799), "S"); T.Eq(Stat.Rank(4800), "SS"); T.Eq(Stat.Rank(5000), "SS"); });
            T.Run("무드 단계 경계(확인된 규칙)", () => { T.Eq(Family.MoodLevel(47), 1); T.Eq(Family.MoodLevel(48), 2); T.Eq(Family.MoodLevel(95), 2); T.Eq(Family.MoodLevel(96), 3); T.Eq(Family.MoodLevel(159), 3); T.Eq(Family.MoodLevel(160), 4); T.Eq(Family.MoodLevel(207), 4); T.Eq(Family.MoodLevel(208), 5); T.Eq(Family.MoodLevel(255), 5); });
            T.Run("원작 가중 게이트 확률 (체력 기준 1980: 미만 1/4, 이상 3/4)", () => {
                var cat = Cat(Bundled()); var ev = cat.Events["sk.s3.good"];
                var f = NewGame.Create(1); var p = f.Members[2]; var rng = new Rng(42);
                foreach (var tc in new[] { new { stamina = 1979, expect = 0.25 }, new { stamina = 1980, expect = 0.75 } })
                {
                    p.Stats[1] = tc.stamina; int hit = 0, N = 20000;
                    for (int i = 0; i < N; i++) { var cx = new RuleContext { Family = f, Rng = rng }; cx.Roles["self"] = p; if (Rules.Eval(ev.Condition, cx)) hit++; }
                    double rate = hit / (double)N;
                    T.True(Math.Abs(rate - tc.expect) < 0.02, "stamina=" + tc.stamina + " 비율 " + rate);
                }
            });
            T.Run("존재하지 않는 역할 참조 시 조건 불충족", () => {
                var cat = Cat(Bundled()); var f = NewGame.Create(1); var cx = new RuleContext { Family = f, Rng = new Rng(1) }; cx.Roles["self"] = f.Members[2];
                T.True(!Rules.Eval(MiniJson.Parse("{\"op\":\"cmp\",\"l\":\"spouse.age\",\"cmp\":\">\",\"r\":1}"), cx));
            });

            T.Run("관계 판정(추정 코드 15/28/16/18) 및 검증", () => {
                var f = NewGame.Create(1); var tar = f.Members[0]; var kid = f.Members[2];
                T.True(Relations.Has(f, tar, "spouse")); T.True(!Relations.Has(f, kid, "spouse"));
                T.True(!Relations.Has(f, tar, "child_spouse")); T.True(!Relations.Has(f, tar, "grandchild"));
                kid.Flags.Add("lover"); T.True(Relations.Has(f, kid, "lover")); kid.SpouseId = 2; T.True(!Relations.Has(f, kid, "lover"));   // 결혼하면 연인 아님
                T.True(Relations.Has(f, tar, "child_spouse"));                                   // 자녀(켄지)가 결혼 → 며느리/사위
                var gk = new Person { Id = 99, Name = "손주", FatherId = kid.Id }; f.Members.Add(gk); T.True(Relations.Has(f, tar, "grandchild"));
                var cx = new RuleContext { Family = f, Rng = new Rng(1) }; cx.Roles["self"] = tar;
                T.True(Rules.Eval(MiniJson.Parse("{\"op\":\"relation\",\"name\":\"spouse\"}"), cx));
                var errs = new List<string>(); Rules.ValidateCondition(MiniJson.Parse("{\"op\":\"relation\",\"name\":\"sibling\"}"), "x", errs); T.True(errs.Count == 1);
            });

            Console.WriteLine("[세션/이벤트]");
            T.Run("신규 이벤트 진행: 페이지 → 선택 → 결과 → 효과·기록", () => {
                var cat = Cat(Bundled()); var f = NewGame.Create(7); var s = new GameSession(f, cat);
                T.True(s.ForceStart("nova.picnic.001", 1)); T.True(s.Paused);
                var v = s.View(); T.Eq(v.Phase, "pages"); T.True(v.Text.Contains("타로가")); T.Eq(v.Origin, "new");
                int mood0 = f.Mood, h0 = f.Members[0].Hearts;
                T.True(!s.Advance()); T.True(s.View().Text.Contains("하나코는"));
                T.True(!s.Advance()); v = s.View(); T.True(v.NeedsChoice); T.Eq(v.Choices.Count, 2);
                T.True(!s.Advance(), "선택 전에는 진행 불가");
                T.True(s.Choose("go")); v = s.View(); T.Eq(v.Phase, "result");
                T.True(s.Advance());   // 결과 마지막 → 종료
                T.True(!s.Paused); T.Eq(f.Mood, mood0 + 12); T.Eq(f.Members[0].Hearts, h0 + 24); T.True(f.Flags.Contains("nova.picnic.done"));
                T.Eq(f.History.Count, 1); T.Eq(f.History[0].Choice, "go");
                T.True(s.LastChanges.Exists(c => c.Key == "mood" && c.Delta == 12), "사건 결과: 무드 +12");
                T.True(s.LastChanges.Exists(c => c.Key == "hearts" && c.PersonId == f.Members[0].Id && c.Delta == 24), "사건 결과: 하트 +24");
                T.True(f.History[0].Changes.Contains("무드↑") && f.History[0].Changes.Contains("하트↑"), "기록에 변화 요약: " + f.History[0].Changes);
            });
            T.Run("원작 난수기: seed×0x6D+0x3FD (ROM 0x08000614)", () => {
                var r = new Rng(1); T.Eq(r.NextU32(), 1u * 0x6Du + 0x3FDu); T.Eq(r.NextU32(), unchecked((1u * 0x6Du + 0x3FDu) * 0x6Du + 0x3FDu));
            });
            T.Run("원작 열중 게이지 하루 규칙 (ROM 0x08027E78 해독)", () => {
                T.Eq(GameSession.ThresholdDays(0, 2), 11); T.Eq(GameSession.ThresholdDays(1, 0), 11);
                T.Eq(GameSession.ThresholdDays(2, 2), 12); T.Eq(GameSession.ThresholdDays(1, 2), 10);
                var cat = Cat(Bundled()); var f = NewGame.Create(11); var s = new GameSession(f, cat); s.EventRateNum = 0;
                var p = f.Members[0]; PlannedStateDef st = null; foreach (var x in cat.States.Values) { st = x; break; }
                p.PlannedStateId = st.Id; p.Gauge = 136; p.InterestDay = 1; p.ArrowFlags = 0; p.PersonalityCode = st.Type;
                // 오르는 기간: 매일 +4~+19
                var rr = new Rng(f.RngState); s.StepDay();
                T.True(p.Gauge >= 140 && p.Gauge <= 155, "오름 +(난수&15)+4: " + p.Gauge); T.Eq(p.InterestDay, 2, "날짜 +1");
                // 힘내라: 화살 표시 3, 매일 +32, 효과 중 재사용 불가
                T.True(Interventions.Use(f, p, "arrow.encourage") == null); T.Eq(p.ArrowFlags, 3);
                T.True(Interventions.Use(f, p, "arrow.calm") != null, "효과 중 재사용 불가(원작 문구)");
                int g0 = p.Gauge; s.StepDay(); T.Eq(p.Gauge, Math.Min(255, g0 + 32), "힘내라 +32");
                // 진정해: 표시 5, 매일 -64 → 0 이면 MIN 사건 차례
                p.ArrowFlags = 0; T.True(Interventions.Use(f, p, "arrow.calm") == null); T.Eq(p.ArrowFlags, 5);
                for (int i = 0; i < 6 && p.Gauge > 0; i++) s.StepDay();
                T.Eq(p.Gauge, 0, "진정해 -64 로 0");
                // 오름 기간이 지나면 −((난수&31)+48)
                p.ArrowFlags = 0; p.Gauge = 200; p.InterestDay = 20; s.StepDay();
                T.True(p.Gauge >= 200 - 79 && p.Gauge <= 200 - 48, "내림 −((난수&31)+48): " + p.Gauge);
                // 255 → MAX 사건: 관심사 종료, 게이지 136·날짜 1·화살 표시 0 으로 다시 시작
                p.Gauge = 255; p.ArrowFlags = 3; var before = p.PlannedStateId;
                for (int i = 0; i < 8 && p.PlannedStateId == before; i++) { s.StepDay(); while (s.Paused) s.Advance(); }
                T.True(p.PlannedStateId != before || p.Gauge == 136, "MAX 사건 후 새로 시작"); T.Eq(p.ArrowFlags & 1, 0, "화살 표시 풀림");
            });
            T.Run("결과 예고: 지금 관심사가 이루어지면 무엇이 오르고 내리는지 (실제 가족은 그대로)", () => {
                var cat = Cat(Bundled()); int checkedN = 0;
                foreach (var st in cat.States.Values)
                {
                    var f = NewGame.Create(5); var s = new GameSession(f, cat); s.EventRateNum = 0;
                    var p = f.Members[0]; p.PlannedStateId = st.Id; p.Gauge = 255; p.ArrowFlags = 0;
                    int mood0 = f.Mood, h0 = p.Hearts;
                    var pr = s.Predict(p.Id, true);
                    T.True(pr != null, "예고 있음"); T.Eq(f.Mood, mood0, "예고는 가족을 바꾸지 않음"); T.Eq(p.Hearts, h0, "예고는 하트를 바꾸지 않음");
                    if (pr.HasRandom || pr.OutcomeId == "") continue;
                    for (int i = 0; i < 3 && !s.Paused; i++) s.StepDay();
                    while (s.Paused) { var v = s.View(); if (v.NeedsChoice) s.Choose(v.Choices[0].Id); else s.Advance(); }
                    string sig(List<EffectChange> l) => string.Join(",", l.Select(c => c.PersonId + c.Key + Math.Sign(c.Delta)));
                    T.Eq(sig(s.LastChanges), sig(pr.Changes), st.Id + " 예고 = 실제");
                    checkedN++;
                }
                T.True(checkedN > 0, "검증한 관심사 없음");
            });
            T.Run("쿨다운·최대 횟수 준수", () => {
                var cat = Cat(Bundled()); var f = NewGame.Create(7); var s = new GameSession(f, cat); s.EventRateNum = 1; s.EventRateDen = 1;
                f.Mood = 200; int fired = 0;
                for (int i = 0; i < 720 && fired < 5; i++)
                {
                    if (s.StepDay() && s.View().EventId == "nova.picnic.001") { fired++; s.Advance(); s.Advance(); s.Choose("stay"); s.Advance(); }
                    else if (s.Paused) { while (!s.Advance()) { if (s.View().NeedsChoice) break; } }
                }
                // 같은 사람에게 720일 쿨다운 → 2명(타로·하나코는 spouse 필요) → 2년 안에 한 사람당 최대 1~2회
                int per = f.FireCount.Where(kv => kv.Key.StartsWith("nova.picnic.001|")).Sum(kv => kv.Value);
                T.True(per >= 1 && per <= 4, "소풍 횟수 " + per);
                foreach (var kv in f.LastFired.Where(kv => kv.Key.StartsWith("nova.picnic.001|"))) { }
            });
            T.Run("예정 상태 → 첫 통과 결과 선택(장기 시뮬레이션, 결정성)", () => {
                string run(ulong seed)
                {
                    var cat = Cat(Bundled()); var f = NewGame.Create(seed); var s = new GameSession(f, cat);
                    for (int i = 0; i < 360 * 30; i++)
                    {
                        if (s.StepDay()) { while (s.Paused) { var v = s.View(); if (v.NeedsChoice) s.Choose(v.Choices[0].Id); else s.Advance(); } }
                    }
                    return string.Join(",", f.History.Select(h => h.Day + ":" + h.EventId + ":" + h.PersonId)) + "|" + f.Mood + "|" + f.Members[2].JobMastery;
                }
                var a = run(99); var b = run(99); var c = run(100);
                T.Eq(a, b, "같은 시드 재현"); T.True(a != c, "다른 시드는 달라야 함");
                T.True(a.Contains("sk."), "원작 구조 이벤트가 발생해야 함: " + a.Substring(0, Math.Min(200, a.Length)));
            });
            T.Run("콘텐츠가 비어도 시간만 진행(예정 상태 없음)", () => {
                var cat = Cat(new List<Pack>()); var f = NewGame.Create(1); var s = new GameSession(f, cat); int d0 = f.Today;
                for (int i = 0; i < 100; i++) T.True(!s.StepDay()); T.Eq(f.Today, d0 + 100);
            });

            Console.WriteLine("[저장]");
            T.Run("저장·복원 라운드트립 (이벤트 진행 중 포함)", () => {
                var dir = Tmp(); var cat = Cat(Bundled()); var f = NewGame.Create(5); var s = new GameSession(f, cat);
                for (int i = 0; i < 400; i++) { if (s.StepDay()) { while (s.Paused) { var v = s.View(); if (v.NeedsChoice) s.Choose(v.Choices[0].Id); else s.Advance(); } } }
                s.ForceStart("nova.picnic.001", 1); s.Advance(); s.Advance();    // 선택지 직전 상태로 저장
                var ss = new SaveSystem(dir); ss.Save("slot1", f, cat);
                var r = ss.Load("slot1");
                T.Eq(MiniJson.Serialize(SaveSystem.ToJson(r.Family, cat, 1).Where(k => k.Key != "savedAt").ToDictionary(k => k.Key, k => k.Value)),
                     MiniJson.Serialize(SaveSystem.ToJson(f, cat, 1).Where(k => k.Key != "savedAt").ToDictionary(k => k.Key, k => k.Value)));
                var s2 = new GameSession(r.Family, cat); T.True(s2.Paused); T.Eq(s2.View().Text, s.View().Text);
                // 같은 난수 흐름으로 이어지는가
                s.Choose(s.View().Choices[0].Id); s2.Choose(s2.View().Choices[0].Id);
                while (s.Paused) s.Advance(); while (s2.Paused) s2.Advance();
                for (int i = 0; i < 200; i++) { s.StepDay(); s2.StepDay(); while (s.Paused) { var v = s.View(); if (v.NeedsChoice) s.Choose(v.Choices[0].Id); else s.Advance(); } while (s2.Paused) { var v = s2.View(); if (v.NeedsChoice) s2.Choose(v.Choices[0].Id); else s2.Advance(); } }
                T.Eq(s.Family.Today, s2.Family.Today); T.Eq(s.Family.History.Count, s2.Family.History.Count); T.Eq(s.Family.Mood, s2.Family.Mood);
            });
            T.Run("여러 슬롯 독립 + 목록", () => {
                var dir = Tmp(); var cat = Cat(Bundled()); var ss = new SaveSystem(dir);
                var a = NewGame.Create(1); a.Name = "A가"; var b = NewGame.Create(2); b.Name = "B가"; b.Today += 100;
                ss.Save("slot1", a, cat); ss.Save("slot2", b, cat); ss.Save("auto", a, cat);
                T.Eq(ss.Load("slot1").Family.Name, "A가"); T.Eq(ss.Load("slot2").Family.Name, "B가"); T.Eq(ss.List().Count, 3);
                ss.Delete("slot1"); T.True(!ss.Exists("slot1"));
            });
            T.Run("주 저장 손상 시 .bak 복원, 둘 다 손상이면 예외", () => {
                var dir = Tmp(); var cat = Cat(Bundled()); var ss = new SaveSystem(dir); var f = NewGame.Create(1);
                ss.Save("slot1", f, cat); f.Today += 10; ss.Save("slot1", f, cat);       // .bak = 이전본
                File.WriteAllText(Path.Combine(dir, "save_slot1.json"), "{broken");
                var r = ss.Load("slot1"); T.True(r.UsedBackup); T.Eq(r.Family.Today, f.Today - 10);
                File.WriteAllText(Path.Combine(dir, "save_slot1.json.bak"), "garbage");
                bool threw = false; try { ss.Load("slot1"); } catch (InvalidDataException) { threw = true; } T.True(threw);
                T.True(ss.List().Single(m => m.Slot == "slot1").Corrupt);
            });
            T.Run("마이그레이션 체인 (v0 → v1, 합성 v1 → v2) 및 미래 버전 거부", () => {
                var dir = Tmp(); var cat = Cat(Bundled());
                // v0 구형 포맷: family.members[].str 필드 이름이 다르고 schema 없음 → v1 로 변환
                var m = new SaveMigrator(2);
                m.Register(0, root => { root["family"] = J.Child(root, "legacyFamily"); return root; });
                m.Register(1, root => { J.Child(root, "family")["name"] = J.Str(J.Child(root, "family"), "name") + "(v2)"; return root; });
                var ss = new SaveSystem(dir, m);
                var legacy = new Dictionary<string, object> { { "legacyFamily", SaveSystem.ToJson(NewGame.Create(3), cat, 1)["family"] } };
                File.WriteAllText(Path.Combine(dir, "save_slot1.json"), MiniJson.Serialize(legacy));
                var r = ss.Load("slot1"); T.Eq(r.FromSchema, 0); T.Eq(r.Family.Name, "다나카 가(v2)"); T.Eq(r.Family.Members.Count, 4);
                var future = SaveSystem.ToJson(NewGame.Create(3), cat, 9); File.WriteAllText(Path.Combine(dir, "save_slot2.json"), MiniJson.Serialize(future));
                bool threw = false; try { ss.Load("slot2"); } catch (InvalidDataException e) { threw = e.Message.Contains("앱을 업데이트"); } T.True(threw);
            });

            Console.WriteLine("[콘텐츠 업데이트]");
            T.Run("신규 팩 설치 → 기존 가족·기록 유지, 새 이벤트 발생 가능", () => {
                var root = Tmp(); var store = new PackStore(root); var bundled = new List<Pack> { LoadPack(Read("sk.sample")) };
                var cat1 = store.LoadCatalog(bundled, new List<string>()); T.True(!cat1.Events.ContainsKey("nova.picnic.001"));
                var f = NewGame.Create(11); var s = new GameSession(f, cat1);
                for (int i = 0; i < 500; i++) { if (s.StepDay()) { while (s.Paused) { var v = s.View(); if (v.NeedsChoice) s.Choose(v.Choices[0].Id); else s.Advance(); } } }
                var ss = new SaveSystem(Tmp()); ss.Save("auto", f, cat1);
                int hist0 = f.History.Count; int day0 = f.Today; string names = string.Join(",", f.Members.Select(m => m.Name + m.Stats[0]));
                var res = store.Install(Read("nova.pack001"), Pack.Hash(Read("nova.pack001")), bundled); T.True(res.Ok, res.Message + string.Join(";", res.Errors));
                var cat2 = store.LoadCatalog(bundled, new List<string>()); T.True(cat2.Events.ContainsKey("nova.picnic.001"));
                var f2 = ss.Load("auto").Family; T.Eq(f2.History.Count, hist0); T.Eq(f2.Today, day0); T.Eq(string.Join(",", f2.Members.Select(m => m.Name + m.Stats[0])), names);
                var s2 = new GameSession(f2, cat2); T.True(s2.ForceStart("nova.picnic.001", 1));
            });
            T.Run("진행 중 이벤트는 팩 업데이트(이벤트 삭제·개정)에도 스냅샷으로 완주", () => {
                var root = Tmp(); var store = new PackStore(root); var bundled = new List<Pack> { LoadPack(Read("sk.sample")) };
                store.Install(Read("nova.pack001"), null, bundled);
                var cat = store.LoadCatalog(bundled, new List<string>());
                var f = NewGame.Create(3); var s = new GameSession(f, cat); s.ForceStart("nova.picnic.001", 1); s.Advance(); s.Advance();   // 선택지 직전
                var ss = new SaveSystem(Tmp()); ss.Save("auto", f, cat);
                // v2: 소풍 이벤트 삭제 + 다른 이벤트 추가
                var v2 = Read("nova.pack001").Replace("\"version\": 1,\n \"title\"", "\"version\": 2,\n \"title\"").Replace("nova.picnic.001", "nova.other.001");
                var res = store.Install(v2, null, bundled); T.True(res.Ok, res.Message + string.Join(";", res.Errors));
                var cat2 = store.LoadCatalog(bundled, new List<string>()); T.True(!cat2.Events.ContainsKey("nova.picnic.001"));
                var f2 = ss.Load("auto").Family; var s2 = new GameSession(f2, cat2);
                T.True(s2.Paused); T.True(s2.View().NeedsChoice); T.True(s2.Choose("go")); T.True(s2.Advance());
                T.Eq(f2.History.Last().EventId, "nova.picnic.001"); T.Eq(f2.History.Last().Choice, "go");
            });
            T.Run("해시 불일치·검증 실패·충돌 팩은 거부하고 기존 상태 유지", () => {
                var root = Tmp(); var store = new PackStore(root); var bundled = new List<Pack> { LoadPack(Read("sk.sample")) };
                T.True(store.Install(Read("nova.pack001"), null, bundled).Ok);
                var before = store.ReadActive()["nova.pack001"];
                var j = Read("nova.pack001");
                var r1 = store.Install(j.Replace("\"version\": 1,\n \"title\"", "\"version\": 2,\n \"title\""), "00" + Pack.Hash(j).Substring(2), bundled); T.True(!r1.Ok && r1.Message.Contains("해시"));
                var bad = j.Replace("\"version\": 1,\n \"title\"", "\"version\": 3,\n \"title\"").Replace("\"op\": \"and\"", "\"op\": \"nope\"");
                var r2 = store.Install(bad, Pack.Hash(bad), bundled); T.True(!r2.Ok);
                var clash = LoadPack(j).RawJson.Replace("nova.pack001", "nova.clash").Replace("nova.picnic.001", "nova.picnic.001");
                var r3 = store.Install(clash, null, bundled); T.True(!r3.Ok && r3.Errors.Any(x => x.Contains("충돌")), string.Join(";", r3.Errors));
                var r4 = store.Install(j, null, bundled); T.True(!r4.Ok, "같은 버전 재설치 거부");
                T.Eq(store.ReadActive()["nova.pack001"], before);
                T.True(store.LoadCatalog(bundled, new List<string>()).Events.ContainsKey("nova.picnic.001"));
            });
            T.Run("설치된 최신 팩이 손상되면 이전 버전으로 롤백", () => {
                var root = Tmp(); var store = new PackStore(root); var bundled = new List<Pack> { LoadPack(Read("sk.sample")) };
                store.Install(Read("nova.pack001"), null, bundled);
                var v2 = Read("nova.pack001").Replace("\"version\": 1,\n \"title\"", "\"version\": 2,\n \"title\"");
                T.True(store.Install(v2, null, bundled).Ok);
                File.WriteAllText(Path.Combine(root, "packs", "nova.pack001", "2.json"), "{ corrupted");
                var warn = new List<string>(); var cat = store.LoadCatalog(bundled, warn);
                T.True(cat.Events.ContainsKey("nova.picnic.001")); T.Eq(cat.Packs.Single(p => p.PackId == "nova.pack001").Version, 1); T.True(warn.Count > 0);
                File.Delete(Path.Combine(root, "packs", "nova.pack001", "1.json"));
                warn.Clear(); cat = store.LoadCatalog(bundled, warn); T.True(!cat.Events.ContainsKey("nova.picnic.001")); T.True(cat.Events.ContainsKey("sk.s1.good"), "번들만으로 동작");
                File.WriteAllText(Path.Combine(root, "active.json"), "garbage"); cat = store.LoadCatalog(bundled, new List<string>()); T.True(cat.Events.ContainsKey("sk.s1.good"));
            });

            Console.WriteLine("[원작 화면 대조]");
            T.Run("달력: 2005-01-01 은 토요일 (원작 HUD '2005년 1월 1일(토)'), 만 나이", () => {
                int d = GameDate.Make(2005, 1, 1); T.Eq(GameDate.Format(d), "2005년 1월 1일(토)");
                T.Eq(GameDate.AgeYears(GameDate.Make(1956, 6, 3), d), 48);      // 원작 상세: 카즈유키 48세
                T.Eq(GameDate.AgeYears(GameDate.Make(1987, 10, 12), d), 17);    // 원작 도입: 잇세이(17세)
                T.Eq(GameDate.Format(GameDate.Make(2005, 2, 30)), "2005년 2월 28일(월)");
            });
            T.Run("화살: 시작 보유 각 5, 미구현 화살 거부", () => {
                var f = NewGame.Create(1); var p = f.Members[0];
                T.Eq(Interventions.Count(f, "arrow.encourage"), 5); T.Eq(Interventions.Count(f, "arrow.calm"), 5);
                T.True(Interventions.Use(f, p, "arrow.encourage") == null); T.Eq(Interventions.Count(f, "arrow.encourage"), 4);
                T.True(Interventions.Use(f, p, "arrow.love") != null, "미구현 화살은 거부");
            });
            T.Run("아이템: 고리 +800(상한 5000), 행복 상자 무드 한 단계", () => {
                var f = NewGame.Create(1); var p = f.Members[0]; f.Items["item.ring.int"] = 2; f.Items["item.happiness_box"] = 1;
                p.Stats[0] = 4500; T.True(Interventions.Use(f, p, "item.ring.int") == null); T.Eq(p.Stats[0], 5000);
                f.Mood = 100; T.True(Interventions.Use(f, p, "item.happiness_box") == null); T.Eq(Family.MoodLevel(f.Mood), 4);
                T.True(Interventions.Use(f, p, "item.happiness_box") != null, "보유 0 이면 거부");
            });
            T.Run("저장 v1 → v2 실제 마이그레이션(시작일·세대주·화살 5개 보충)", () => {
                var dir = Tmp(); var cat = Cat(Bundled()); var f = NewGame.Create(3);
                var v1 = SaveSystem.ToJson(f, cat, 1); var fam = J.Child(v1, "family");
                foreach (var k in new[] { "startDay", "gratitude", "head", "items" }) fam.Remove(k);
                File.WriteAllText(Path.Combine(dir, "save_slot1.json"), MiniJson.Serialize(v1));
                var r = new SaveSystem(dir).Load("slot1"); T.Eq(r.FromSchema, 1);
                T.Eq(r.Family.StartDay, f.Today); T.Eq(Interventions.Count(r.Family, "arrow.calm"), 5); T.Eq(r.Family.HeadId, f.Members[0].Id);
            });
            T.Run("원작 시작 가족 형식(start_family.json) 불러오기 + 관계 추정", () => {
                var json = "{\"date\":[2005,1,1],\"head\":0,\"mood\":128,\"house\":2,\"assets\":5000,\"members\":[" +
                    "{\"name\":\"가\",\"birth\":[1956,6,3],\"gender\":0,\"stats\":[2173,2408,1617,3400],\"job\":24,\"character\":\"c1\",\"plannedStateId\":\"planned-X\",\"plannedTitle\":\"고민 중\"}," +
                    "{\"name\":\"나\",\"birth\":[1957,12,14],\"gender\":1,\"stats\":[1,2,3,4],\"job\":26}," +
                    "{\"name\":\"다\",\"birth\":[1987,10,12],\"gender\":0,\"stats\":[1,2,3,4],\"job\":10}]}";
                var f = NewGame.FromOriginal(J.Obj(MiniJson.Parse(json)), 9);
                T.Eq(f.Name, "가 가"); T.Eq(f.Members[0].SpouseId, f.Members[1].Id); T.Eq(f.Members[2].FatherId, f.Members[0].Id); T.Eq(f.Members[2].MotherId, f.Members[1].Id);
                T.Eq(Stat.Rank(f.Members[0].Stats[3]), "A"); T.Eq(f.Members[0].Character, "c1"); T.Eq(f.Members[0].PlannedTitle, "고민 중");
                var s = new GameSession(f, Cat(Bundled())); for (int i = 0; i < 60; i++) { s.StepDay(); while (s.Paused) { var v = s.View(); if (v.NeedsChoice) s.Choose(v.Choices[0].Id); else s.Advance(); } }
                T.True(f.Members[0].PlannedStateId != "planned-X", "카탈로그에 없는 원작 예정 상태는 기한 뒤 해제");
            });

            T.Run("맵은 팩 데이터: 방 정의 검증(용도·겹침·범위)", () => {
                string Map(string rooms) { return "{\"format\":1,\"packId\":\"t.maps\",\"version\":1,\"maps\":[{\"id\":\"house\",\"name\":\"집\",\"art\":\"house_day\",\"width\":960,\"rooms\":[" + rooms + "]}]}"; }
                var ok = new List<string>(); var p = Pack.Load(Map("{\"id\":\"a\",\"kind\":\"bedroom\",\"x\":[0,100]},{\"id\":\"b\",\"kind\":\"living\",\"x\":[100,200]}"), ok);
                T.True(p != null, string.Join("|", ok)); var c = Cat(new List<Pack> { p }); T.True(c.Maps["house"].FirstOfKind("living") != null);
                var e1 = new List<string>(); T.True(Pack.Load(Map("{\"id\":\"a\",\"kind\":\"spaceship\",\"x\":[0,100]}"), e1) == null); T.True(e1.Exists(x => x.Contains("spaceship")));
                var e2 = new List<string>(); T.True(Pack.Load(Map("{\"id\":\"a\",\"kind\":\"bedroom\",\"x\":[0,100]},{\"id\":\"b\",\"kind\":\"living\",\"x\":[90,200]}"), e2) == null); T.True(e2.Exists(x => x.Contains("겹침")));
                var e3 = new List<string>(); T.True(Pack.Load(Map("{\"id\":\"a\",\"kind\":\"bedroom\",\"x\":[900,1000]}"), e3) == null);
            });

            Console.WriteLine("[화면 비율]");
            T.Run("세로 비율별 레이아웃 (16:9 ~ 9:21, 소형·대형, 노치 안전영역)", () => {
                var cases = new[] { // w,h px, dpi, 안전영역 inset(top,bottom)
                    new[]{720f,1280f,320f,0,0}, new[]{1080f,1920f,420f,0,0}, new[]{1080f,2160f,420f,0,0}, new[]{1080f,2340f,440f,66f,0},
                    new[]{1080f,2400f,420f,100f,48f}, new[]{1440f,3120f,560f,120f,60f}, new[]{1080f,2520f,420f,90f,40f}, new[]{480f,800f,160f,0,0},
                    new[]{800f,1280f,213f,0,0}, new[]{1668f,2388f,264f,24f,20f}, new[]{320f,568f,160f,20f,0} };
                foreach (var c in cases)
                {
                    var r = LayoutCalculator.Compute(c[0], c[1], 0, c[3], c[0], c[1] - c[3] - c[4], c[2]);
                    string tag = c[0] + "x" + c[1];
                    T.True(r.Valid, tag + " 장면 영역 부족 scene=" + r.Scene.H / r.Dp + "dp");
                    var seq = new[] { r.TopBar, r.Scene, r.FamilyStrip, r.EventPanel, r.Controls };
                    float y = c[3]; foreach (var q in seq) { T.True(Math.Abs(q.Y - y) < 0.01f, tag + " 영역 간 틈/겹침"); y += q.H; }
                    T.True(Math.Abs(y - (c[1] - c[4])) < 0.5f, tag + " 안전영역을 채우지 못함");
                    T.True(r.Controls.H / r.Dp >= LayoutCalculator.MinTouchDp, tag + " 조작 바 높이 < 48dp");
                    T.True(r.EventPanel.H / r.Dp >= 120f, tag + " 대화창 너무 낮음"); T.True(r.HouseScale >= c[0] / 240f * 0.74f, tag + " 집 그림이 너무 작음");
                }
            });

            T.Run("캐릭터 조합 규칙: 목·얼굴 기준점 배치, 좌우반전, 그리는 순서 (합성 데이터)", () => {
                // 2x2 파트들로 만든 가짜 라이브러리: 몸통 목=(Ox+0, Oy-16), 얼굴 기준점 표로 눈·코·입·머리 위치 결정
                string Part(int w, int h, int ax, int ay, int off, params int[] ext) { return "{\"w\":" + w + ",\"h\":" + h + ",\"ax\":" + ax + ",\"ay\":" + ay + ",\"off\":" + off + ",\"ext\":[" + string.Join(",", ext) + "]}"; }
                string Grp(string part) { return "{\"rom\":\"x\",\"parts\":[" + part + "," + part + "]}"; }
                var px = new List<byte>();
                int Add(params byte[] b) { int o = px.Count; px.AddRange(b); return o; }
                int body = Add(11, 11, 11, 11), face = Add(6, 6, 6, 6), eye = Add(9, 0, 0, 0), hairF = Add(3, 3, 0, 0), hairB = Add(5, 5, 5, 5);
                var blocks = new List<string>(); for (int i = 0; i < 49; i++) blocks.Add(Grp(Part(2, 2, 1, 0, body, 16, 16)));
                var json = "{\"categories\":{" +
                    "\"body\":[" + string.Join(",", blocks) + "]," +
                    "\"face\":[" + Grp(Part(2, 2, 33, 16, face, 16, 25, 28, 31, 33, 1, 18)) + "]," +
                    "\"eyes\":[" + Grp(Part(2, 2, 1, 0, eye)) + "],\"nose\":[" + Grp(Part(0, 0, 0, 0, 0)) + "],\"mouth\":[" + Grp(Part(0, 0, 0, 0, 0)) + "]," +
                    "\"hairfront\":[" + Grp(Part(2, 2, 1, 0, hairF)) + "],\"hairback\":[" + Grp(Part(2, 2, 1, 0, hairB, 16, 0)) + "]}," +
                    "\"palettes\":{\"base\":[0,0,32767],\"hair\":[[1,2,3]],\"skin\":[[4,5,6,7,8]],\"outfitTable\":[[0,0,0,0],[9,9,9,9]]}}";
                var lib = PartsLibrary.Load(json, px.ToArray());
                T.True(lib.Available, "라이브러리 로드");
                var look = new CharacterLook();
                var c = CharacterComposer.Compose(lib, look);
                int W = CharacterComposer.Width;
                // 몸통: 좌상단 (16-1, 64-16-0) = (15,48)
                T.Eq((int)c[48 * W + 15], 11, "몸통 위치");
                // 목 = (16, 48), ref = 48-33 = 15. 얼굴 좌상단 = (16-1, 15+16-2) = (15,29)
                T.Eq((int)c[29 * W + 15], 6, "얼굴 위치");
                // 눈 기준점 (16, 15+25=40) → 좌상단 (15,40), 왼쪽 위 픽셀만 9
                T.Eq((int)c[40 * W + 15], 9, "눈 위치"); T.Eq((int)c[40 * W + 16], 0, "눈 투명 픽셀은 그리지 않음");
                look.EyesFlip = true; c = CharacterComposer.Compose(lib, look);
                // 반전: ax = 2-1 = 1 → 좌상단 (15,40), 픽셀은 오른쪽으로
                T.Eq((int)c[40 * W + 16], 9, "눈 좌우반전"); T.Eq((int)c[40 * W + 15], 0, "눈 좌우반전(원래 자리)");
                // 앞머리 기준점 (16, 15+16=31) → (15,31) 은 얼굴 위에 그려진다
                T.Eq((int)c[31 * W + 15], 3, "앞머리가 얼굴 위");
                // 뒷머리 기준점 (16, 15+16+16-0=47) → (15,47) — 몸통(48행)이 뒷머리 위에 그려져야 한다
                T.Eq((int)c[47 * W + 15], 5, "뒷머리 위치"); T.Eq((int)c[48 * W + 15], 11, "몸통이 뒷머리 위");
                var pal = lib.Palette(new CharacterLook { HairColor = 0, SkinColor = 0, OutfitColor = 0, Outfit = 0 });
                T.Eq(pal[3], 1, "머리색 칸 3"); T.Eq(pal[6], 4, "피부색 칸 6"); T.Eq(pal[11], 9, "의상색 표 항목 1");
                var back = CharacterLook.FromJson(J.Obj(MiniJson.Parse(MiniJson.Serialize(look.ToJson()))));
                T.Eq(back.EyesFlip, true, "저장 왕복"); T.Eq(back.HairColor, -1, "색 미지정 유지");
            });

            T.Run("내가 아는 가족: 원작 입력 항목 → 가족 생성(관계·능력 순위·저장 왕복)", () => {
                var fs = new FamilySetup { Surname = "야마다" };
                var g = fs.Add(FamilyRole.Grandfather); g.Name = "겐";
                var gm = fs.Add(FamilyRole.Grandmother); gm.Name = "우메";
                var mo = fs.Add(FamilyRole.Mother); mo.Name = "요코"; mo.Blood = "AB"; mo.Personality = 2; mo.AbilityRank = new[] { 4, 3, 2, 1 };
                var d = fs.Add(FamilyRole.Child, 1); d.Name = "미키";
                T.Eq(GameDate.Year(g.BirthDay), 1968, "조부 기본 생년(원작 관찰 1968)");
                T.Eq(GameDate.Year(d.BirthDay), 2004, "자녀 기본 생년(원작 관찰 2004)");
                T.True(!FamilySetup.AsksCharacter(d, fs.StartDay), "6세 이하는 캐릭터 선택 없음");
                d.BirthDay = GameDate.Make(1992, 1, 1);
                T.True(FamilySetup.AsksCharacter(d, fs.StartDay), "7세 이상은 선택");
                T.Eq(FamilySetup.GalleryRole(d, fs.StartDay), "daughter13", "13세 딸 목록");
                d.BirthDay = GameDate.Make(1995, 1, 1); T.Eq(FamilySetup.GalleryRole(d, fs.StartDay), "daughter7", "10세 딸 목록");
                T.Eq(FamilySetup.GalleryRole(g, fs.StartDay), "grandfather", "조부 목록");
                var bad = new FamilySetup(); var x = bad.Add(FamilyRole.Father); x.Name = "a"; x.AbilityRank = new[] { 1, 1, 2, 3 };
                T.True(bad.Validate().Count > 0, "능력 순위 중복 거부");
                var f = fs.Build(42);
                T.Eq(f.Members.Count, 4, "인원");
                var pm = f.Members[2]; var pd = f.Members[3];
                T.Eq(f.Members[0].SpouseId, f.Members[1].Id, "조부모 부부");
                T.Eq(pd.MotherId, pm.Id, "어머니 → 딸");
                T.Eq(pm.FatherId, f.Members[0].Id, "조부 → 어머니(아버지 칸 없을 때)");
                T.Eq(f.HeadId, pm.Id, "세대주 = 어머니(아버지 없음)");
                T.True(pm.Stats[3] > pm.Stats[0], "운 1순위 > 지력 4순위");
                T.Eq(f.Name, "야마다 가", "가족 성");
                var back = Person.FromJson(J.Obj(MiniJson.Parse(MiniJson.Serialize(pm.ToJson()))));
                T.Eq(back.Blood, "AB", "혈액형 저장"); T.Eq(back.Personality, 2, "성격 저장");
            });

            var localArt = Environment.GetEnvironmentVariable("SK_LOCAL_ART");
            var galleryDir = Environment.GetEnvironmentVariable("SK_GALLERY_IDX");
            if (!string.IsNullOrEmpty(localArt) && File.Exists(Path.Combine(localArt, "parts.json")))
                T.Run("원작 갤러리 재현: C# 조합 결과 = 파이썬 기준 구현 (로컬 추출물 있을 때만)", () => {
                    var lib = PartsLibrary.Load(File.ReadAllText(Path.Combine(localArt, "parts.json")), File.ReadAllBytes(Path.Combine(localArt, "parts.bin.bytes")));
                    T.True(lib.Available && lib.Presets.Count > 0, "파트·프리셋 로드");
                    int exact = 0, total = 0;
                    var root = J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(localArt, "parts.json"))));
                    foreach (var kv in J.Child(root, "presets"))
                    {
                        var d = J.Obj(kv.Value); var f = Path.Combine(galleryDir ?? "", kv.Key + ".idx");
                        if (!File.Exists(f)) continue;
                        var img = File.ReadAllBytes(f).Skip(4).ToArray();
                        var c = CharacterComposer.Compose(lib, lib.Presets[kv.Key], lib.PresetAges[kv.Key]);
                        int diff = 0; for (int i = 0; i < c.Length; i++) if (c[i] != img[i]) diff++;
                        T.Eq(diff, J.Int(d, "diffPixels"), kv.Key + " 픽셀 차이 수가 파이썬과 다름");
                        total++; if (diff == 0) exact++;
                    }
                    var refPath = Path.Combine(localArt, "compose_ref.json");
                    if (File.Exists(refPath))
                    {
                        int ok = 0, n = 0;
                        foreach (var o in J.Arr(MiniJson.Parse(File.ReadAllText(refPath))))
                        {
                            var d = J.Obj(o); var look = CharacterLook.FromJson(J.Child(d, "look"));
                            var ag = AgeSlots.FromJson(J.Get(d, "age"), AgeSlots.Adult);
                            var px = CharacterComposer.Compose(lib, look, ag, (CharacterComposer.Pose)J.Int(d, "pose"), J.Int(d, "set"));
                            var h = BitConverter.ToString(System.Security.Cryptography.MD5.HashData(px)).Replace("-", "").ToLowerInvariant();
                            n++; if (h == J.Str(d, "md5")) ok++;
                        }
                        T.Eq(ok, n, "자세·연령 칸 조합이 파이썬 기준과 다름");
                        Console.WriteLine("       자세·연령 칸 무작위 " + n + "건 C# = 파이썬");
                    }
                    T.True(total > 0, "갤러리 캡처 없음");
                    Console.WriteLine("       갤러리 " + total + "명 중 원작과 픽셀 완전 일치 " + exact + "명");
                });

            var origDir = Environment.GetEnvironmentVariable("SK_ORIG_DIR");
            if (!string.IsNullOrEmpty(origDir) && File.Exists(Path.Combine(origDir, "vectors.json")))
                T.Run("원작 규칙 재현: 판정 트리·관계 슬롯·관심사 선택 = 원작 ROM 실행 결과 (로컬 자료 있을 때만)", () => OrigVectors(origDir));
            if (!string.IsNullOrEmpty(origDir) && File.Exists(Path.Combine(origDir, "orig_rules.json")) && File.Exists(Path.Combine(origDir, "orig_text.json")))
                T.Run("원작 세션(앱 연결): 추천 가족 → 2년 진행 → 사건 대사 → 저장·불러오기 후 같은 진행 (로컬)", () =>
                {
                    var rules = SennenKazoku.Core.Orig.OrigRules.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(origDir, "orig_rules.json")))));
                    rules.TreesText = File.ReadAllBytes(Path.Combine(origDir, rules.TreesFile));
                    var text = SennenKazoku.Core.Orig.OrigText.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(origDir, "orig_text.json")))));
                    var s = SennenKazoku.Core.Orig.OrigSession.NewRecommended(rules, text, "김", 0x1234);
                    T.True(s.Family.Members.Count >= 2, "가족 인원 " + s.Family.Members.Count);
                    Console.WriteLine("       시작 " + GameDate.Format(s.Family.Today) + " " + string.Join(", ", s.Family.Members.ConvertAll(p => p.Name + "(" + (p.Gender == 0 ? "남" : "여") + p.Age(s.Family.Today) + "세 " + p.PlannedTitle + ")")));
                    int shown = 0, withText = 0, pagesTotal = 0; string sample = null;
                    void Drain(SennenKazoku.Core.Orig.OrigSession ss)
                    {
                        while (ss.Paused)
                        {
                            var v = ss.View(); pagesTotal++;
                            if (ss == s && sample == null && v.TextSource != "none" && v.Text.Length > 20) sample = v.Title + " / " + v.Text.Replace("\n", " ");
                            if (ss.Advance()) { shown++; if (v.TextSource != "none") withText++; }
                        }
                    }
                    for (int d = 0; d < 730; d++) { s.StepDay(); Drain(s); }
                    Console.WriteLine("       2년: 보여 준 사건 " + shown + "개 (대사 있음 " + withText + "), 장 " + pagesTotal + ", " + GameDate.Format(s.Family.Today));
                    Console.WriteLine("       예: " + sample);
                    T.True(shown > 100 && withText * 10 >= shown * 9, "사건 수·대사 비율");
                    // 저장 → 불러오기 → 두 세션을 60일 같이 진행: 같은 사건이 나야 한다
                    s.PrepareSave();
                    var json = MiniJson.Serialize(SaveSystem.ToJson(s.Family, null, SaveSystem.CurrentSchema));
                    var f2 = SaveSystem.FromJson(J.Obj(MiniJson.Parse(json)));
                    var s2 = SennenKazoku.Core.Orig.OrigSession.Load(rules, text, f2);
                    var a = new List<string>(); var b = new List<string>();
                    for (int d = 0; d < 60; d++)
                    {
                        s.StepDay(); while (s.Paused) { var v = s.View(); a.Add(GameDate.Format(s.Family.Today) + v.EventId); s.Advance(); }
                        s2.StepDay(); while (s2.Paused) { var v = s2.View(); b.Add(GameDate.Format(s2.Family.Today) + v.EventId); s2.Advance(); }
                    }
                    T.True(a.Count > 0 && string.Join("|", a) == string.Join("|", b), "불러온 뒤 진행이 다름 " + a.Count + "/" + b.Count);
                    T.True(Convert.ToBase64String(s.Game.Mem.SaveBlock()) == Convert.ToBase64String(s2.Game.Mem.SaveBlock()), "60일 뒤 원작 메모리가 다름");
                    Console.WriteLine("       저장·불러오기 뒤 60일 사건 " + a.Count + "개 같음, 원작 메모리 같음");
                });
            var years = Environment.GetEnvironmentVariable("SK_ORIG_YEARS");
            if (!string.IsNullOrEmpty(origDir) && !string.IsNullOrEmpty(years))
                T.Run("원작 코드로 새 가족 장기 진행 (로컬)", () =>
                {
                    var rules = SennenKazoku.Core.Orig.OrigRules.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(origDir, "orig_rules.json")))));
                    rules.TreesText = File.ReadAllBytes(Path.Combine(origDir, rules.TreesFile));
                    var m = new SennenKazoku.Core.Orig.OrigMem(); var vm = rules.CreateVm(m);
                    SennenKazoku.Core.Orig.OrigNewGame.BlankCartridge(vm);
                    SennenKazoku.Core.Orig.OrigNewGame.TitleNewGame(vm);
                    SennenKazoku.Core.Orig.OrigNewGame.Recommended(vm);
                    var g = new SennenKazoku.Core.Orig.OrigGame(m, rules);
                    var sw = System.Diagnostics.Stopwatch.StartNew(); int nev = 0, days = int.Parse(years) * 365;
                    var kinds = new Dictionary<uint, int>(); var perYear = new int[int.Parse(years) + 1];
                    string Fam() { var l = new List<string>(); for (int k = 0; k < 8; k++) { uint p = SennenKazoku.Core.Orig.OrigMem.PersonAddr(k); if (SennenKazoku.Core.Orig.OrigGame.Present(m, k)) { SennenKazoku.Core.Orig.OrigDate.Get(m, p + 0x2E, out int by, out _, out _); l.Add(m.R16(p + 0x3C) + "(" + (m.R8(p + 0x31) == 0 ? "남" : "여") + by + ")"); } } return string.Join(" ", l); }
                    string last = Fam(); Console.WriteLine("       시작: " + last);
                    // 검증용 기록: SK_REC_DIR 에 SK_REC_FROM 날부터 SK_REC_DAYS 날 동안 맨 바깥 원작 함수 호출의 앞뒤 메모리를 남긴다 (romlift/replay_check.py 가 원작 ROM 실행과 비교)
                    var recDir = Environment.GetEnvironmentVariable("SK_REC_DIR"); int recFrom = int.Parse(Environment.GetEnvironmentVariable("SK_REC_FROM") ?? "0"), recDays = int.Parse(Environment.GetEnvironmentVariable("SK_REC_DAYS") ?? "1"), recN = 0; int curDay = 0;
                    byte[] Snap() { var b = new byte[0x40000 + 0x8000 + 0x40000 + 0x400]; Array.Copy(m.Ewram, 0, b, 0, 0x40000); Array.Copy(m.Iwram, 0, b, 0x40000, 0x8000); Array.Copy(m.Frames, 0, b, 0x48000, 0x40000); Array.Copy(m.Io, 0, b, 0x88000, 0x400); return b; }
                    byte[] recPre = null;
                    if (!string.IsNullOrEmpty(recDir))
                    {
                        Directory.CreateDirectory(recDir);
                        g.Vm.BeforeTop = (fn, a) => { if (curDay >= recFrom && curDay < recFrom + recDays) recPre = Snap(); };
                        g.Vm.AfterTop = (fn, a, r) =>
                        {
                            if (recPre == null) return;
                            using (var w = new BinaryWriter(File.Create(Path.Combine(recDir, "rec_" + (recN++).ToString("D5") + ".bin"))))
                            {
                                w.Write(0x31434552u); w.Write(uint.TryParse(fn.StartsWith("0x") ? fn.Substring(2) : fn, System.Globalization.NumberStyles.HexNumber, null, out var fa) ? fa : fn == "slots" ? 0x08110B90u : 0u); w.Write((uint)curDay); w.Write((uint)a.Length); foreach (var x in a) w.Write(x); w.Write(r);
                                w.Write(recPre); w.Write(Snap());
                            }
                            recPre = null;
                        };
                    }
                    // 검증용: SK_TRACE_SCHED 가 있으면 일정 풀 목록 머리(0x02038F70~)를 바꾼 맨 바깥 호출을 적는다
                    if (Environment.GetEnvironmentVariable("SK_TRACE_SCHED") != null && g.Vm.AfterTop == null)
                    {
                        byte[] heads = new byte[14];
                        g.Vm.BeforeTop = (fn, a) => Array.Copy(m.Ewram, 0x38F70, heads, 0, 14);
                        g.Vm.AfterTop = (fn, a, r) =>
                        {
                            for (int k = 0; k < 14; k++) if (m.Ewram[0x38F70 + k] != heads[k])
                                {
                                    var sb = new System.Text.StringBuilder();
                                    for (int i = 0; i < 12; i++) { var e = new byte[12]; Array.Copy(m.Ewram, 0x38BEC + 12 * i, e, 0, 12); if (e[8] != 0x12 && e[0] != 0xFF) sb.Append(" [" + i + ":" + BitConverter.ToString(e).Replace("-", "") + "]"); }
                                    Console.WriteLine("         일정 일" + curDay + " " + fn + "(" + string.Join(",", Array.ConvertAll(a, x => x.ToString("X"))) + ") 머리 " + BitConverter.ToString(heads) + " → " + BitConverter.ToString(m.Ewram, 0x38F70, 14) + sb);
                                    break;
                                }
                        };
                    }
                    for (int dd = 0; dd < days; dd++)
                    {
                        curDay = dd;
                        g.NextDate();
                        try
                        {
                            foreach (var e in g.TickDay())
                            {
                                nev++; kinds[e.Type] = kinds.TryGetValue(e.Type, out var c) ? c + 1 : 1; perYear[dd / 365]++;
                                if (Environment.GetEnvironmentVariable("SK_ORIG_LOGEV") != null && dd >= int.Parse(Environment.GetEnvironmentVariable("SK_ORIG_LOGEV")) && dd < int.Parse(Environment.GetEnvironmentVariable("SK_ORIG_LOGEV")) + int.Parse(Environment.GetEnvironmentVariable("SK_ORIG_LOGEV_N") ?? "30"))
                                {
                                    uint pp = SennenKazoku.Core.Orig.OrigMem.PersonAddr(Math.Max(0, e.Person));
                                    Console.WriteLine("         일" + dd + " 인물" + e.Person + " 종류" + e.Type + " 코드" + e.Code + " 결과" + e.Data.ToString("X8") + " 관심사 " + m.R16(pp + 0x80) + "," + m.R16(pp + 0x82) + " 게이지 " + m.R8(pp + 0x5A) + " +5F " + m.R8(pp + 0x5F) + " 일정칸 " + string.Join(",", Enumerable.Range(0, 10).Select(k => m.R8(pp + 0x76 + (uint)k).ToString("X2"))) + " 풀0 " + m.R32(0x02038BEC).ToString("X8") + " 풀1 " + m.R32(0x02038BEC + 12).ToString("X8"));
                                }
                            }
                        }
                        catch (SennenKazoku.Core.Orig.OrigUnmodeled ex)
                        {
                            SennenKazoku.Core.Orig.OrigDate.Get(m, SennenKazoku.Core.Orig.OrigMem.Date, out int ey, out int em, out int ed);
                            Console.WriteLine("       " + ey + "-" + em + "-" + ed + " 멈춤: " + ex.Message); break;
                        }
                        var f = Fam();
                        if (f != last) { SennenKazoku.Core.Orig.OrigDate.Get(m, SennenKazoku.Core.Orig.OrigMem.Date, out int ey, out int em, out int ed); Console.WriteLine("       " + ey + "-" + em + "-" + ed + " 가족: " + f); last = f; }
                    }
                    Console.WriteLine("       " + days + "일, 사건 " + nev + " (종류별 " + string.Join(",", kinds.Select(kv => kv.Key + ":" + kv.Value)) + "), " + sw.ElapsedMilliseconds + "ms");
                    Console.WriteLine("       해마다 사건: " + string.Join(" ", perYear));
                });
            var daysDir = Environment.GetEnvironmentVariable("SK_DAYS_DIR");
            if (!string.IsNullOrEmpty(origDir) && !string.IsNullOrEmpty(daysDir) && Directory.Exists(daysDir))
                T.Run("원작 하루 진행 비교: 날 바뀜 덤프 → C# 하루 → 다음 날 바뀜 덤프 (로컬 자료 있을 때만)", () => OrigDays(origDir, daysDir));

            Console.WriteLine("\n통과 " + T.Pass + " / 실패 " + T.Fail);
            return T.Fail == 0 ? 0 : 1;
        }
    }
}

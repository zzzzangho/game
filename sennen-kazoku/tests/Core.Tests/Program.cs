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
            var only = Environment.GetEnvironmentVariable("SK_ONLY");   // 이름에 이 글이 든 검사만 (빠른 반복용)
            if (!string.IsNullOrEmpty(only) && !name.Contains(only)) return;
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
        /// <summary>시험용: 원작 메모리로 원작 세션 만들기 (세이브 영역만 옮김).</summary>
        static SennenKazoku.Core.Family SessionFixture(SennenKazoku.Core.Orig.OrigMem m, SennenKazoku.Core.Family f)
        {
            var blk = m.SaveBlock(); var b = new byte[blk.Length + 5]; Array.Copy(blk, b, blk.Length);
            Array.Copy(BitConverter.GetBytes(m.R32(SennenKazoku.Core.Orig.OrigMem.Seed)), 0, b, blk.Length, 4);
            f.OrigState = Convert.ToBase64String(b); return f;
        }

        /// <summary>시험용 PNG 읽기 (8비트 RGB/RGBA, 필터 0~4) — 0xRRGGBB, 투명은 −1.</summary>
        /// <summary>조합 그림(색 번호)과 실기 캡처(회색 99,99,99 = 빈칸)를 ±4·±8 밀어 맞춰 색까지 같은 픽셀 비율(천분율)의 최댓값.</summary>
        static int PixMatch(byte[] io, int[] po, string f)
        {
            var co = ReadPng(f, out int ow, out int oh); int bo = -1;
            for (int dy = -8; dy <= 8; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int ok = 0, tot = 0;
                    for (int y = 0; y < 64; y++) for (int x = 0; x < 32; x++)
                    {
                        int v = io[y * 32 + x]; int X = x + dx, Y = y + dy;
                        int c = X >= 0 && X < ow && Y >= 0 && Y < oh ? co[Y * ow + X] : -1;
                        bool capOn = c >= 0 && c != 0x636363;
                        if (v == 0 && !capOn) continue;
                        tot++;
                        if (v != 0 && capOn)
                        {
                            int q = po[v]; int r = (q & 31) * 255 / 31, g = ((q >> 5) & 31) * 255 / 31, b = ((q >> 10) & 31) * 255 / 31;
                            if (Math.Abs(r - (c >> 16 & 255)) <= 10 && Math.Abs(g - (c >> 8 & 255)) <= 10 && Math.Abs(b - (c & 255)) <= 10) ok++;
                        }
                    }
                    if (tot > 0) bo = Math.Max(bo, ok * 1000 / tot);
                }
            return bo;
        }

        static int[] ReadPng(string path, out int w, out int h)
        {
            var d = File.ReadAllBytes(path); int p = 8; w = h = 0; int ct = 2; var idat = new MemoryStream();
            while (p < d.Length)
            {
                int len = d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3]; string t = System.Text.Encoding.ASCII.GetString(d, p + 4, 4);
                if (t == "IHDR") { w = d[p + 8] << 24 | d[p + 9] << 16 | d[p + 10] << 8 | d[p + 11]; h = d[p + 12] << 24 | d[p + 13] << 16 | d[p + 14] << 8 | d[p + 15]; ct = d[p + 17]; }
                if (t == "IDAT") idat.Write(d, p + 8, len);
                p += 12 + len;
            }
            idat.Position = 2;
            var raw = new MemoryStream(); using (var z = new System.IO.Compression.DeflateStream(idat, System.IO.Compression.CompressionMode.Decompress)) z.CopyTo(raw);
            var r = raw.ToArray(); int bpp = ct == 6 ? 4 : 3, stride = w * bpp; var cur = new byte[stride]; var prev = new byte[stride]; var o = new int[w * h];
            for (int y = 0; y < h; y++)
            {
                int f = r[y * (stride + 1)];
                for (int i = 0; i < stride; i++)
                {
                    int x = r[y * (stride + 1) + 1 + i], a = i >= bpp ? cur[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                    int pa = Math.Abs(b - c), pb = Math.Abs(a - c), pc = Math.Abs(a + b - 2 * c);
                    int pr = f == 0 ? 0 : f == 1 ? a : f == 2 ? b : f == 3 ? (a + b) / 2 : (pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                    cur[i] = (byte)(x + pr);
                }
                for (int x = 0; x < w; x++) o[y * w + x] = bpp == 4 && cur[x * 4 + 3] == 0 ? -1 : cur[x * bpp] << 16 | cur[x * bpp + 1] << 8 | cur[x * bpp + 2];
                var t2 = prev; prev = cur; cur = t2;
            }
            return o;
        }

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

            // 인물 팔레트(0x08099448): 실기 사건 장면에서 잇세이(인물 3) 그림의 OBJ 팔레트 (그림자 버퍼 0x02005318, 칸 1~14)
            {
                var pm = Fresh("main.ram"); var pvm = rules.CreateVm(pm);
                uint rec = SennenKazoku.Core.Orig.OrigMem.PersonAddr(3), buf = 0x0F03B200, look = 0x0F03B300;
                for (uint i = 0; i < 16; i++) pm.W8(look + i, pm.R8(rec + i));
                ushort[] want = { 0x0000, 0x7fff, 0x6f7a, 0x4650, 0x2126, 0x6fbf, 0x5f7f, 0x42be, 0x2e17, 0x5e7f, 0x673a, 0x7fff, 0x7232, 0x594d };
                for (uint i = 0; i < 32; i++) pm.W8(buf + i, 0);
                pvm.Call("08099448", buf, look + 8, look, 4, 0xFF, 0);   // 나이 구분 4(18세), 옷 인자 0xFF, 마지막 인자 0 (실기 사건 장면과 같은 값)
                int okp = 0; for (uint i = 1; i <= 14; i++) if (pm.R16(buf + 2 * i) == want[i - 1]) okp++;
                Console.WriteLine("       인물 팔레트(0x08099448): 실기 사건 그림 팔레트와 같은 칸 " + okp + "/14");
                T.Eq(okp, 14, "인물 팔레트가 실기와 다름");
                // 몸통 세트 0~3(가족 +0x682 — 세기가 바뀐 뒤 1~3)·몸통 번호 0~83(사건 옷 포함)·나이 구분 0~6 의 색이 팩 ROM 조각만으로 계산돼야 한다.
                // (세트 4~7 은 뒷모습 묶음이라 원작이 색 함수에 넘기지 않는다 — 뒷모습 색도 세트 0 으로 계산, 실기 확인)
                int pcalls = 0; string pmiss = "";
                for (uint set = 0; set < 4 && pmiss == ""; set++)
                    for (uint bodyNo = 0; bodyNo < 84 && pmiss == ""; bodyNo++)
                        for (uint cls = 0; cls < 7 && pmiss == ""; cls++)
                        {
                            pm.W8(look + 7, bodyNo);
                            try { pvm.Call("08099448", buf, look + 8, look, cls, 0, set); pcalls++; }
                            catch (Exception ex) { pmiss = "세트 " + set + " 몸통 " + bodyNo + " 구분 " + cls + ": " + ex.Message; }
                        }
                Console.WriteLine("       인물 색: 몸통 세트 0~3 × 몸통 84 × 나이 구분 7 = " + pcalls + "번 계산" + (pmiss == "" ? "" : " — 실패 " + pmiss));
                T.True(pmiss == "", "인물 색 계산에 팩에 없는 ROM: " + pmiss);
            }
            // 원작 외형 → 앱 조합: 실기 사건 장면의 잇세이(인물 3, 말풍선 없는 동작) 그림과 픽셀 색 비교 (로컬 캡처 cap_actor3.png 가 있을 때)
            if (File.Exists(Path.Combine(dir, "cap_actor3.png")) && File.Exists(Path.Combine(dir, "parts.json")))
            {
                var lib = SennenKazoku.Core.PartsLibrary.Load(File.ReadAllText(Path.Combine(dir, "parts.json")), File.ReadAllBytes(Path.Combine(dir, "parts.bin.bytes")));
                var cm = Fresh("main.ram");
                var fam = new SennenKazoku.Core.Family { Name = "t" };
                var os3 = SennenKazoku.Core.Orig.OrigSession.Load(rules, new SennenKazoku.Core.Orig.OrigText(), SessionFixture(cm, fam));
                var look = os3.OrigLook(3);
                var idx = SennenKazoku.Core.CharacterComposer.Compose(lib, look, SennenKazoku.Core.AgeSlots.ForAge(18), SennenKazoku.Core.CharacterComposer.Pose.FrontA, 0);
                var pal = lib.Palette(look);
                var cap = ReadPng(Path.Combine(dir, "cap_actor3.png"), out int cw, out int ch);
                int best = -1, bt = 0;
                for (int dy = -8; dy <= 8; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int ok = 0, tot = 0;
                        for (int y = 0; y < 64; y++) for (int x = 0; x < 32; x++)
                        {
                            int v = idx[y * 32 + x]; int X = x + dx, Y = y + dy;
                            int c = X >= 0 && X < cw && Y >= 0 && Y < ch ? cap[Y * cw + X] : -1;
                            bool capOn = c >= 0 && c != 0x636363;
                            if (v == 0 && !capOn) continue;
                            tot++;
                            if (v != 0 && capOn)
                            {
                                int q = pal[v]; int r = (q & 31) * 255 / 31, g = ((q >> 5) & 31) * 255 / 31, b = ((q >> 10) & 31) * 255 / 31;
                                if (Math.Abs(r - (c >> 16 & 255)) <= 10 && Math.Abs(g - (c >> 8 & 255)) <= 10 && Math.Abs(b - (c & 255)) <= 10) ok++;
                            }
                        }
                        if (tot > 0 && ok * 1000 / tot > best) { best = ok * 1000 / tot; bt = tot; }
                    }
                foreach (int pn in new[] { 0, 1 })   // 집 화면 캡처(시작 가족 아버지 48세·어머니 47세 — 나이 구분 5)
                {
                    string cf = Path.Combine(dir, "cap_house" + pn + ".png"); if (!File.Exists(cf)) continue;
                    var lk = os3.OrigLook(pn); int age = pn == 0 ? 48 : 47;
                    var cp = ReadPng(cf, out int w2, out int h2); int bestP = -1;
                    foreach (var pose in new[] { SennenKazoku.Core.CharacterComposer.Pose.FrontA, SennenKazoku.Core.CharacterComposer.Pose.FrontB })
                    {
                        var ix = SennenKazoku.Core.CharacterComposer.Compose(lib, lk, SennenKazoku.Core.AgeSlots.ForAge(age), pose, 0); var pl = lib.Palette(lk);
                        int ok = 0, tot = 0;
                        for (int i = 0; i < 32 * 64 && i < cp.Length; i++)
                        {
                            bool on = cp[i] >= 0; if (ix[i] == 0 && !on) continue; tot++;
                            if (ix[i] != 0 && on) { int q = pl[ix[i]]; int r = (q & 31) * 255 / 31, g = ((q >> 5) & 31) * 255 / 31, b = ((q >> 10) & 31) * 255 / 31; int c = cp[i];
                                if (Math.Abs(r - (c >> 16 & 255)) <= 10 && Math.Abs(g - (c >> 8 & 255)) <= 10 && Math.Abs(b - (c & 255)) <= 10) ok++; }
                        }
                        bestP = Math.Max(bestP, tot > 0 ? ok * 1000 / tot : 0);
                    }
                    Console.WriteLine("       원작 외형 조합(인물 " + pn + ", " + age + "세): 집 화면 캡처와 색까지 같은 픽셀 " + bestP / 10.0 + "%");
                    T.True(bestP >= 980, "원작 외형 조합이 집 화면 캡처와 다름");
                }
                // 사건 옷차림: 실기 사건 장면(인물 3)에서 레코드 몸통 바이트와 장면 토큰 1A 0E 01 의 옷 인자를 바꿔 찍은 그림(cap_outfit_<몸통>_<옷>.png)
                foreach (var f in Directory.GetFiles(dir, "cap_outfit_*.png"))
                {
                    var nm = Path.GetFileNameWithoutExtension(f).Split('_'); int bodyB = int.Parse(nm[2]), od = int.Parse(nm[3]);
                    uint ra = SennenKazoku.Core.Orig.OrigMem.PersonAddr(3); var om = os3.Game.Mem; uint keep = om.R8(ra + 7); om.W8(ra + 7, (uint)bodyB);
                    var lo = os3.OrigLook(3, od, 0); om.W8(ra + 7, keep);
                    var io = SennenKazoku.Core.CharacterComposer.Compose(lib, lo, SennenKazoku.Core.AgeSlots.ForAge(18), SennenKazoku.Core.CharacterComposer.Pose.FrontA, 0);
                    int bo = PixMatch(io, lib.Palette(lo), f);
                    Console.WriteLine("       사건 옷차림(몸통 바이트 " + bodyB + ", 옷 " + od + " → 몸통 자원 " + lo.OutfitBody + "): 실기 그림과 색까지 같은 픽셀 " + bo / 10.0 + "%");
                    T.True(bo >= 980, "사건 옷차림이 실기 그림과 다름");
                }
                // 장면 토큰 1A 0E 01 (칸, b, c, 옷): c = 1 뒷모습(뒷모습 걷기 첫 자세), b 비트0 = 좌우 반전 — 실기 캡처 cap_pose_*.png
                foreach (var (pf, od, back, flip) in new[] { ("back", 0xFF, true, false), ("backout", 3, true, false), ("mirror", 0xFF, false, true) })
                {
                    string f = Path.Combine(dir, "cap_pose_" + pf + ".png"); if (!File.Exists(f)) continue;
                    var lo = os3.OrigLook(3, od, 0);
                    var io = SennenKazoku.Core.CharacterComposer.Compose(lib, lo, SennenKazoku.Core.AgeSlots.ForAge(18), back ? SennenKazoku.Core.CharacterComposer.Pose.BackA : SennenKazoku.Core.CharacterComposer.Pose.FrontA, 0);
                    if (flip) io = SennenKazoku.Core.CharacterComposer.Mirror(io);
                    int bo = PixMatch(io, lib.Palette(lo), f);
                    Console.WriteLine("       사건 인물 자세(" + pf + "): 실기 그림과 색까지 같은 픽셀 " + bo / 10.0 + "%");
                    T.True(bo >= 980, "사건 인물 자세가 실기 그림과 다름: " + pf);
                }
                // 6세 이하: 실기 사건 장면(인물 3)의 생일·성별을 바꿔 찍은 그림 cap_child_<나이>_<성별>[_back].png
                foreach (var f in Directory.GetFiles(dir, "cap_child_*.png"))
                {
                    var nm = Path.GetFileNameWithoutExtension(f).Split('_'); int years = int.Parse(nm[2]), gen = int.Parse(nm[3]); bool back = nm.Length > 4 && nm[4] == "back";
                    if (nm.Length > 4 && !back) continue;
                    uint ra = SennenKazoku.Core.Orig.OrigMem.PersonAddr(3); var om = os3.Game.Mem;
                    var keep = new uint[4]; for (uint i = 0; i < 3; i++) keep[i] = om.R8(ra + 0x2E + i); keep[3] = om.R8(ra + 0x31);
                    SennenKazoku.Core.Orig.OrigDate.Set(om, ra + 0x2E, 2004 - years, 10, 12); om.W8(ra + 0x31, (uint)gen);
                    var lo = os3.OrigLook(3, 0xFE, 0);
                    for (uint i = 0; i < 3; i++) om.W8(ra + 0x2E + i, keep[i]); om.W8(ra + 0x31, keep[3]);
                    var io = SennenKazoku.Core.CharacterComposer.Compose(lib, lo, SennenKazoku.Core.AgeSlots.ForAge(years), back ? SennenKazoku.Core.CharacterComposer.Pose.BackA : SennenKazoku.Core.CharacterComposer.Pose.FrontA, 0);
                    int bo = PixMatch(io, lib.Palette(lo), f);
                    Console.WriteLine("       6세 이하 그림(" + years + "세, 성별 " + gen + (back ? ", 뒷모습" : "") + "): 실기 그림과 색까지 같은 픽셀 " + bo / 10.0 + "%");
                    T.True(bo >= 980, "6세 이하 그림이 실기와 다름");
                }
                // 뒤 무늬 움직임 표(event_art.py, 로컬): 햇살 0x08887F94 = 6장 × 16프레임(실기 2프레임 간격 추적과 같음)
                if (File.Exists(Path.Combine(dir, "ev_back_anim.json.bytes")))
                {
                    var btab = SennenKazoku.Core.SceneAnim.BackTable.Parse(File.ReadAllText(Path.Combine(dir, "ev_back_anim.json.bytes")));
                    var seen = new List<int>(); int pv = -9;
                    for (int fr = 0; fr < 96; fr++) { int b = btab.Frame("08887F94", fr); if (b != pv) { seen.Add(b); pv = b; } }
                    Console.WriteLine("       뒤 무늬 움직임: 무늬 " + btab.Runs.Count + "가지, 햇살 순서 " + string.Join(",", seen));
                    T.True(seen.Count == 6 && btab.Frame("08887F94", 0) == btab.Frame("08887F94", 96) && btab.Frame("08887F94", 15) != btab.Frame("08887F94", 16), "뒤 무늬 움직임 표");
                }
                // 말풍선 움직임 표(event_art.py, 로컬): ♪(02) 한 주기 = 실기 매 프레임 추적 작게4·빈4·C6·D8·C6·E8·C6·D8·C6·E8·C6·D8·C6·빈4·작게4·숨김30
                if (File.Exists(Path.Combine(dir, "ev_emo_anim.json.bytes")))
                {
                    var et = SennenKazoku.Core.SceneAnim.EmotionTable.Parse(File.ReadAllText(Path.Combine(dir, "ev_emo_anim.json.bytes")));
                    var sb2 = new System.Text.StringBuilder(); int prevF = -9;
                    for (int fr = 0; fr < 122; fr++) { int f2 = et.Frame(2, fr); if (f2 != prevF) { sb2.Append(f2).Append(' '); prevF = f2; } }
                    Console.WriteLine("       말풍선 ♪ 그림 순서: " + sb2 + "(감정 " + et.Runs.Count + "가지)");
                    T.True(sb2.ToString() == "0 1 2 3 2 4 2 3 2 4 2 3 2 1 0 -1 " && et.Frame(2, 122) == 0 && et.Frame(2, 121) == -1 && et.Runs.Count == 21, "말풍선 움직임 표");
                }
                // 수염(레코드 +3 = 부품 분류 3): 인물 3 을 40세로 바꾸고 +3 을 바꿔 찍은 실기 그림 cap_extra_<나이>_<값>.png
                foreach (var f in Directory.GetFiles(dir, "cap_extra_*.png"))
                {
                    var nm = Path.GetFileNameWithoutExtension(f).Split('_'); int years = int.Parse(nm[2]), xv = int.Parse(nm[3]);
                    uint ra = SennenKazoku.Core.Orig.OrigMem.PersonAddr(3); var om = os3.Game.Mem;
                    var keep = new uint[4]; for (uint i = 0; i < 3; i++) keep[i] = om.R8(ra + 0x2E + i); keep[3] = om.R8(ra + 3);
                    SennenKazoku.Core.Orig.OrigDate.Set(om, ra + 0x2E, 2004 - years, 10, 12); om.W8(ra + 3, (uint)xv);
                    var lo = os3.OrigLook(3, 0xFE, 0);
                    for (uint i = 0; i < 3; i++) om.W8(ra + 0x2E + i, keep[i]); om.W8(ra + 3, keep[3]);
                    var io = SennenKazoku.Core.CharacterComposer.Compose(lib, lo, SennenKazoku.Core.AgeSlots.ForAge(years), SennenKazoku.Core.CharacterComposer.Pose.FrontA, 0);
                    int bo = PixMatch(io, lib.Palette(lo), f);
                    Console.WriteLine("       수염(" + years + "세, +3 = " + xv + "): 실기 그림과 색까지 같은 픽셀 " + bo / 10.0 + "%");
                    T.True(bo >= 980, "수염가 실기와 다름");
                }
                Console.WriteLine("       원작 외형 조합(인물 3, 얼굴 " + look.Face + " 머리 " + look.Hair + " 눈 " + look.Eyes + " 코 " + look.Nose + " 입 " + look.Mouth + " 몸통 " + look.Body + "): 실기 그림과 색까지 같은 픽셀 " + best / 10.0 + "%");
                T.True(best >= 980, "원작 외형 조합이 실기 그림과 다름");
            }
            // 종합 진단(0x0806614C): 원작 ROM 을 같은 RAM 덤프에서 돌린 값 (unicorn) — main.ram 3·3·3·3, custom_create.ram 3·1·4·2
            {
                var d1 = SennenKazoku.Core.Orig.OrigSession.Diagnosis(rules.CreateVm(Fresh("main.ram")));
                string d2s = "";
                if (File.Exists(Path.Combine(dir, "custom_create.ram"))) d2s = string.Join("·", SennenKazoku.Core.Orig.OrigSession.Diagnosis(rules.CreateVm(Fresh("custom_create.ram"))));
                Console.WriteLine("       종합 진단: main " + string.Join("·", d1) + ", custom_create " + d2s + "  (원작 ROM: 3·3·3·3, 3·1·4·2)");
                T.True(string.Join("·", d1) == "3·3·3·3" && (d2s == "" || d2s == "3·1·4·2"), "종합 진단이 원작과 다름");
            }
            // 자기 방(0x08017EF0 → 장면+0x184, 0x080171FC): 실기 집 화면 RAM(house_r2.ram) 의 장면+0x184 = 00 00 01 02 FF…  → 집 2(방 종류 0,1,4,4,4…) 방 칸 2·2·3·4
            if (File.Exists(Path.Combine(dir, "house_r2.ram")))
            {
                var hm = Fresh("house_r2.ram"); var hvm = rules.CreateVm(hm);
                var rooms = SennenKazoku.Core.Orig.OrigSession.OwnRooms(hvm);
                string tab = ""; for (uint n = 0; n < 8; n++) tab += hm.R8(SennenKazoku.Core.Orig.OrigSession.RoomScene + 0x184 + n).ToString("X2") + " ";
                Console.WriteLine("       자기 방: 표 " + tab + "→ 방 칸 " + string.Join(",", rooms) + "  (실기 00 00 01 02 FF FF FF FF)");
                T.Eq(tab.Trim(), "00 00 01 02 FF FF FF FF", "자기 방 표가 실기와 다름");
                T.Eq(string.Join(",", rooms), "2,2,3,4,-1,-1,-1,-1", "자기 방 칸이 실기와 다름");
                // 방 자리(0x0801729C): 실기 작업 큐의 걷기 목표 x — 방 2 자리 0 = 424, 방 2 자리 2 = 448, 방 4 자리 0 = 216
                {
                    var sx = new List<string>();
                    foreach (var rs in new[] { new[] { 2, 0 }, new[] { 2, 2 }, new[] { 4, 0 } }) { SennenKazoku.Core.Orig.OrigSession.RoomSpot(hm, rs[0], rs[1], out int x0, out int t0); sx.Add(x0.ToString()); }
                    Console.WriteLine("       방 자리 x: " + string.Join(",", sx) + " (실기 424,448,216)");
                    T.Eq(string.Join(",", sx), "424,448,216", "방 자리 x 가 실기와 다름");
                }
                // 하루 일정 고르기(0x08011FB4~0x080120B6): 실기 작업 버퍼 +0xDB8 이 규칙의 결과(직업 일정이면 0x08119E34 의 난수 3택 중 하나)와 맞는지
                var hg = new SennenKazoku.Core.Orig.OrigGame(hm, rules); var hres = new List<string>();
                foreach (uint task in new uint[] { 0x0200F124, 0x02011960, 0x0201419C, 0x020169D8 })
                {
                    uint p = hm.R32(task + 4), job = hm.R8(p + 0x58), hw = hm.R32(task + 0xDB8);
                    if (hm.R32(task + 0xDBC) != 0) { hres.Add("사건 있음"); continue; }
                    var cand = new List<uint>(); string why;
                    if (hm.R8(p + 0x5F) != 0) { cand.Add(hvm.Call("08119E10", 0x6C)); why = "특별"; }
                    else if (((1u << (int)hg.Weekday()) & hvm.Call("0811A19C", job) & 0xFF) != 0 && hvm.Call("08119E78", p + 0x80) != 0) { cand.Add(hvm.Call("08119E78", p + 0x80)); why = "관심사"; }
                    else { uint jr = hm.R32(0x0889D3B8 + 4 * job); for (uint k = 0; k < 3; k++) cand.Add(hm.R32(jr + 0x10 + 4 * k)); why = "직업 3택"; }
                    hres.Add(why + (cand.Contains(hw) ? " 맞음" : " 다름 " + hw.ToString("X8")));
                    T.True(cand.Contains(hw), "하루 일정 고르기가 실기와 다름 (작업 " + task.ToString("X8") + ")");
                }
                Console.WriteLine("       하루 일정 고르기(실기 작업 4개, 요일 " + hg.Weekday() + "): " + string.Join(", ", hres));
            }
            // 집 감정 말풍선(0x080133EA~): 실기 집 화면 RAM(house_emo_r400.ram) 의 말풍선 스프라이트 움직임 번호 — 인물 0 = 4(♫), 1 = 3(초록 ♪), 3 = 3
            if (File.Exists(Path.Combine(dir, "house_emo_r400.ram")))
            {
                var em = Fresh("house_emo_r400.ram"); var evm = rules.CreateVm(em);
                string got = string.Join(",", new[] { 0, 1, 3 }.Select(id => SennenKazoku.Core.Orig.OrigSession.HouseEmote(evm, id, false)));
                Console.WriteLine("       집 감정 말풍선(인물 0,1,3): " + got + " (실기 4,3,3)");
                T.Eq(got, "4,3,3", "집 감정 말풍선이 실기와 다름");
                // 조건을 바꾼 실기 화면(house_emo.py): 랭크 7 + 관심사 (1,70) → 7 하트 · (1,22) → 9 전구 · (3,0) → 2 · (2,175) → 6 · 꿈 0 + (0,16) → 8
                const uint R1 = 0x0202CA94; var cases = new List<string>();
                foreach (var c in new[] { new[] { 1, 70, -1 }, new[] { 1, 22, -1 }, new[] { 3, 0, -1 }, new[] { 2, 175, -1 }, new[] { 0, 16, 0 } })
                {
                    var cm2 = Fresh("house_emo_r400.ram"); var cvm2 = rules.CreateVm(cm2);
                    cm2.W16(0x0202C66E, 7); cm2.W16(R1 + 0x80, (uint)c[0]); cm2.W16(R1 + 0x82, (uint)c[1]); if (c[2] >= 0) cm2.W16(R1 + 0x3E, (uint)c[2]);
                    cases.Add(SennenKazoku.Core.Orig.OrigSession.HouseEmote(cvm2, 1, false).ToString());
                }
                Console.WriteLine("       조건을 바꾼 말풍선: " + string.Join(",", cases) + " (실기 7,9,2,6,8)");
                T.Eq(string.Join(",", cases), "7,9,2,6,8", "조건별 집 감정 말풍선이 실기와 다름");
            }
            // 세기 확인(0x080A113C, 자동 변환 — 점프 표 뒤 반복문 머리를 찾도록 lift.py 를 고친 뒤 변환됨): 시작일을 1·2·4·7세기 전으로 바꾼 RAM 에서
            // 원작 ROM 을 unicorn 으로 돌린 결과(century/c<세기>.out.ram)와 저장 영역 0x0202C010~0x0203C440·결과 8바이트가 같아야 한다.
            if (Directory.Exists(Path.Combine(dir, "century")))
            {
                int okc = 0, nc = 0; var log = new List<string>();
                foreach (var f in Directory.GetFiles(Path.Combine(dir, "century"), "c*.in.ram"))
                {
                    string name = Path.GetFileName(f).Replace(".in.ram", "");
                    var cm2 = Fresh(Path.Combine("century", name + ".in.ram")); var cvm = rules.CreateVm(cm2);
                    cvm.Call("080A113C", 0x0203FF00);
                    var want = File.ReadAllBytes(Path.Combine(dir, "century", name + ".out.ram"));
                    int bad = 0; for (int i = 0x2C010; i < 0x3C440; i++) if (cm2.Ewram[i] != want[i]) bad++;
                    for (int i = 0x3FF00; i < 0x3FF08; i++) if (cm2.Ewram[i] != want[i]) bad++;
                    nc++; if (bad == 0) okc++;
                    log.Add(name + ":" + (bad == 0 ? "같음" : bad + "바이트 다름") + "(+0x682 " + cm2.Ewram[0x2C692] + ")");
                }
                Console.WriteLine("       세기 확인 0x080A113C = 원작 ROM: " + string.Join(", ", log));
                T.Eq(okc, nc, "세기 확인이 원작 ROM 실행과 다름");
            }
            // 세대(0x08025DFC): 실기 선대의 마음 목록 — 번호 0·1 "1대", 번호 2 "2대" (같은 main.state, 족보 아버지 칸을 지우면 2 → 1대)
            {
                var gm = Fresh("main.ram"); var gvm = rules.CreateVm(gm);
                uint g0 = gvm.Call("08025DFC", 0), g1 = gvm.Call("08025DFC", 1), g2 = gvm.Call("08025DFC", 2);
                gm.W16(SennenKazoku.Core.Orig.OrigMem.Gene + 2 * SennenKazoku.Core.Orig.OrigMem.GeneSize + 0x10, 0xFFFF); uint g2b = gvm.Call("08025DFC", 2);
                Console.WriteLine("       세대: 번호0 " + g0 + "대, 번호1 " + g1 + "대, 번호2 " + g2 + "대, 번호2(아버지 칸 지움) " + g2b + "대  (실기: 1·1·2·1)");
                T.True(g0 == 1 && g1 == 1 && g2 == 2 && g2b == 1, "세대가 실기와 다름");
            }
            // 원작 코드로 60일 진행 (관심사 하루 처리 → 사건 → 다음 관심사). 예외 없이 돌고 사건이 나야 한다.
            {
                var mem = Fresh("main.ram");
                var game = new SennenKazoku.Core.Orig.OrigGame(mem, rules);
                var sw = System.Diagnostics.Stopwatch.StartNew(); int nev = 0, days = 60;
                var titles = new List<string>(); string gratLog = null, gratText = null; int shownN = 0;
                var otext = File.Exists(Path.Combine(dir, "orig_text.json")) ? SennenKazoku.Core.Orig.OrigText.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "orig_text.json"))))) : null;
                for (int dday = 0; dday < days; dday++)
                {
                    uint pts0 = mem.R32(0x0202C670);
                    foreach (var ev in game.TickDay())
                    {
                        nev++;
                        if (ev.Shown.Length > 0) shownN++;
                        if (otext != null && otext.HasCharset && gratText == null && ev.Points1 > ev.Points0)
                        {
                            gratText = string.Join(" / ", otext.Decode(ev.Shown, a => "이름" + a, id => "인물" + id));
                            if (Environment.GetEnvironmentVariable("SK_SHOWN_HEX") != null)
                                for (int q = 0; q + 3 < ev.Shown.Length; q++) if (ev.Shown[q] == 0x1A && (ev.Shown[q + 1] == 5 || ev.Shown[q + 1] == 6)) Console.WriteLine("       토큰 " + BitConverter.ToString(ev.Shown, q, 4));
                        }
                        uint p = SennenKazoku.Core.Orig.OrigMem.PersonAddr(ev.Person);
                        titles.Add("일" + dday + " 인물" + ev.Person + " 큐종류" + ev.Type + " 코드" + ev.Code + " 결과 " + ev.Data.ToString("X8") + " → 관심사 " + mem.R16(p + 0x80) + "," + mem.R16(p + 0x82) + " 게이지 " + mem.R8(p + 0x5A));
                    }
                    if (gratLog == null && mem.R32(0x0202C670) != pts0)
                        gratLog = "일" + dday + ": 감사의 마음 " + pts0 + "→" + mem.R32(0x0202C670) + ", 랭크 " + mem.R16(0x0202C66E) + ", 화살 " + mem.R8(0x0202C640) + "·" + mem.R8(0x0202C641)
                            + ", 인물0 감사 " + (mem.R8(SennenKazoku.Core.Orig.OrigMem.PersonAddr(0) + 0x65) | mem.R8(SennenKazoku.Core.Orig.OrigMem.PersonAddr(0) + 0x66) << 8);
                    game.NextDate();
                }
                // 실기(같은 시작 main.state, 자동 진행): 인물0(힘내라 화살 효과 중)의 관심사가 이루어지며 감사의 마음 0→5·인물0 감사 5, 이어서 랭크 0→1, 화살 4·5 → 10·10
                Console.WriteLine("       첫 감사: " + (gratLog ?? "없음") + "  (실기: 감사의 마음 0→5, 랭크 1, 화살 10·10, 인물0 감사 5)");
                Console.WriteLine("       결과 문구가 있는 사건 " + shownN + "번, 첫 감사 사건의 결과 문구: " + (gratText ?? "(글자표 없음)").Replace("\n", " "));
                T.True(gratLog != null && gratLog.Contains("0→5, 랭크 1, 화살 10·10, 인물0 감사 5"), "감사·랭크 진행이 실기와 다름: " + gratLog);
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

        /// <summary>
        /// 실기 덤프(사건 메모 장면의 하루 묶음 함수 0x080CB74C 에 들어가는 순간, c_NN_080cb74e.ram — 장면 객체 r0)에서 C# 으로 그 함수를 한 번 돌려
        /// 다음 덤프와 세이브 영역·장면 객체를 비교한다. 두 덤프 사이에는 화면만 도는 프레임이 있다(난수는 이 함수와 그 아래에서만 쓴다고 보고 비교).
        /// </summary>
        static void OrigOffline(string dir, string offDir)
        {
            SennenKazoku.Core.Orig.OrigVm.Trail = Environment.GetEnvironmentVariable("SK_TRACE") != null;
            var rules = SennenKazoku.Core.Orig.OrigRules.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "orig_rules.json")))));
            rules.TreesText = File.ReadAllBytes(Path.Combine(dir, rules.TreesFile));
            var shared = new SennenKazoku.Core.Orig.OrigMem(); rules.CreateVm(shared);
            var files = Directory.GetFiles(offDir, "c_*_080cb74e.ram").OrderBy(f => f).ToList();
            const uint E = SennenKazoku.Core.Orig.OrigMem.EwramBase;
            const uint obj = SennenKazoku.Core.Orig.OrigOffline.Obj;
            SennenKazoku.Core.Orig.OrigMem Load(string f)
            {
                var d = File.ReadAllBytes(f); var mm = new SennenKazoku.Core.Orig.OrigMem(); mm.ShareRom(shared);
                Array.Copy(d, mm.Ewram, SennenKazoku.Core.Orig.OrigMem.EwramSize);
                Array.Copy(d, (int)SennenKazoku.Core.Orig.OrigMem.EwramSize, mm.Iwram, 0, mm.Iwram.Length); return mm;
            }
            int Diff(SennenKazoku.Core.Orig.OrigMem mm, byte[] want, byte[] was, List<string> runs)
            {
                int nd = 0;
                for (uint x = E; x < E + SennenKazoku.Core.Orig.OrigMem.EwramSize; x++)
                {
                    if (!OfflineScope(x, obj) || mm.Ewram[x - E] == want[x - E]) continue;
                    uint y = x; while (y + 1 < E + SennenKazoku.Core.Orig.OrigMem.EwramSize && mm.Ewram[y + 1 - E] != want[y + 1 - E]) y++;
                    for (uint z = x; z <= y; z++) if (OfflineScope(z, obj)) nd++;
                    if (runs.Count < 40) runs.Add((x >= obj && x < obj + 0x10000 ? "장면+" + (x - obj).ToString("X") : x.ToString("X8")) + (y > x ? "~" + (y - x + 1) : "") + ":" + mm.Ewram[x - E].ToString("X2") + "/" + want[x - E].ToString("X2") + "(" + was[x - E].ToString("X2") + ")");
                    x = y;
                }
                return nd;
            }
            // ① 하루 묶음 하나씩: 덤프 k 에서 Step → 덤프 k+1
            int same = 0;
            for (int i = 0; i + 1 < files.Count; i++)
            {
                var m = Load(files[i]); var vm = rules.CreateVm(m);
                uint r;
                try { r = SennenKazoku.Core.Orig.OrigOffline.Attach(vm).Step(); }
                catch (SennenKazoku.Core.Orig.OrigUnmodeled ex) { Console.WriteLine("       " + Path.GetFileName(files[i]) + " 옮기지 못함: " + ex.Message); continue; }
                var runs = new List<string>(); int nd = Diff(m, File.ReadAllBytes(files[i + 1]), File.ReadAllBytes(files[i]), runs);
                if (nd == 0) same++;
                Console.WriteLine("       " + Path.GetFileName(files[i]) + " 돌려준 값 " + r + " 다른 바이트 " + nd + " " + string.Join(" ", runs));
            }
            Console.WriteLine("       하루 묶음 " + (files.Count - 1) + "번 중 원작과 같음 " + same);
            // ② 장면 시작(0x080C9488 첫 호출 덤프) → C# Begin(일 수는 실기 값) → 첫 하루 묶음 호출 덤프, ③ 이어서 Step 만으로 끝 덤프까지
            var init = Directory.GetFiles(offDir, "c_*_080c948a.ram").OrderBy(f => f).FirstOrDefault();
            bool beginOk = true, chainOk = true;
            if (init != null && files.Count > 0)
            {
                var m = Load(init); var vm = rules.CreateVm(m);
                var b0 = File.ReadAllBytes(files[0]);
                uint days = BitConverter.ToUInt32(b0, (int)(obj + 0x9944 - E));
                var o = SennenKazoku.Core.Orig.OrigOffline.Begin(vm, days);
                var runs = new List<string>(); int nd = Diff(m, b0, File.ReadAllBytes(init), runs);
                beginOk = nd == 0;
                Console.WriteLine("       장면 시작(일 수 " + days + "): 다른 바이트 " + nd + " " + string.Join(" ", runs));
                var bis = Environment.GetEnvironmentVariable("SK_OFF_COPY");   // 진단: "시작~끝" 16진 범위를 첫 묶음 덤프에서 복사
                if (!string.IsNullOrEmpty(bis))
                    foreach (var rg in bis.Split(','))
                    {
                        var ab = rg.Split('-'); uint lo = Convert.ToUInt32(ab[0], 16), hi = Convert.ToUInt32(ab[1], 16);
                        for (uint x = lo; x < hi; x++) if (x >= 0x03000000) m.Iwram[x & 0x7FFF] = File.ReadAllBytes(files[0])[(int)SennenKazoku.Core.Orig.OrigMem.EwramSize + (int)(x & 0x7FFF)]; else m.Ewram[x - E] = b0[x - E];
                    }
                for (int i = 1; i < files.Count; i++)
                {
                    try { o.Step(); } catch (SennenKazoku.Core.Orig.OrigUnmodeled ex) { Console.WriteLine("       이어 하기 " + i + " 옮기지 못함: " + ex.Message); chainOk = false; break; }
                    var r2 = new List<string>(); int n2 = Diff(m, File.ReadAllBytes(files[i]), File.ReadAllBytes(files[i - 1]), r2);
                    if (n2 != 0) { chainOk = false; Console.WriteLine("       시작부터 이어 하기 " + i + "번째 묶음 다른 바이트 " + n2 + " " + string.Join(" ", r2)); break; }
                }
                if (chainOk) Console.WriteLine("       시작부터 이어 하기: 묶음 " + (files.Count - 1) + "번 모두 원작과 같음");
            }
            // ④ 보고 글 고르기(0x080D5CE0): 실기 덤프(보고 함수 첫 호출 d_NN_080d5ce2) → C# 으로 계속 불러 고른 글(글 포인터·인물)을 실기 기록(msg.log)과 비교
            bool repOk = true;
            var rdir = Path.Combine(offDir, "r");
            if (Directory.Exists(rdir) && File.Exists(Path.Combine(rdir, "msg.log")))
            {
                var lines = File.ReadAllLines(Path.Combine(rdir, "msg.log"));
                var want = new List<string>(); uint rep = 0; int first = -1;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("pc=080d5ce2")) { if (first < 0) { first = i; rep = Convert.ToUInt32(lines[i].Split(' ').First(x => x.StartsWith("r0=")).Substring(3), 16); } continue; }
                    if (first >= 0 && lines[i].Contains("pc=080a60b2") && lines[i].Contains("lr=080d"))
                        want.Add(lines[i].Split(' ').First(x => x.StartsWith("r1=")).Substring(3) + "/" + lines[i].Split(' ').First(x => x.StartsWith("r0=")).Substring(3));
                }
                var dumps = Directory.GetFiles(rdir, "d_*_080d5ce2.ram").OrderBy(f => f).ToList();
                if (dumps.Count > 0 && rep != 0)
                {
                    var m = Load(dumps[0]); var vm = rules.CreateVm(m);
                    var got = new List<string>();
                    for (int k = 0; k < 40; k++)
                    {
                        vm.Messages = new List<uint[]>();
                        vm.Call("080D5CE0", rep);
                        if (vm.Messages.Count == 0) break;
                        foreach (var a in vm.Messages) got.Add(a[1].ToString("x8") + "/" + a[0].ToString("x8"));
                    }
                    repOk = string.Join(" ", got) == string.Join(" ", want);
                    Console.WriteLine("       보고 글(실기): " + string.Join(" ", want) + "\n       보고 글(C#):   " + string.Join(" ", got));
                }
            }
            T.True(repOk, "사건 메모 보고 글 순서가 실기와 다름");
            // ⑤ 세대교체 보고 "출발" 뒤 천계(진입값 4): 실기 천계 첫 프레임 덤프(h6/d_*) → C# 천계 글 번호를 실기 글 기록(h7/h.log, 371 은 그 앞)과 비교
            var hdump = Directory.Exists(Path.Combine(offDir, "h6")) ? Directory.GetFiles(Path.Combine(offDir, "h6"), "d_*.ram").OrderBy(f => f).FirstOrDefault() : null;
            bool hvOk = true;
            if (hdump != null && File.Exists(Path.Combine(offDir, "h7", "ids.txt")) && File.Exists(Path.Combine(dir, "orig_text.json")))
            {
                var text = SennenKazoku.Core.Orig.OrigText.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "orig_text.json")))));
                var m = Load(hdump); var vm = rules.CreateVm(m);
                var h = new SennenKazoku.Core.Orig.OrigHeaven(vm, text, text.Heaven, id => "○", "신님");
                h.RunAfterWedding(4);
                var got = new List<int>(); foreach (var pg in h.Pages) if (pg.MsgId >= 0 && (got.Count == 0 || got[got.Count - 1] != pg.MsgId)) got.Add(pg.MsgId);
                // 실기 글 번호 (h.log 의 글 포인터를 ROM 글 표로 바꾼 것 — 도구가 로컬에서 만든 ids.txt, 371 은 중단점을 걸기 전)
                var idsFile = Path.Combine(offDir, "h7", "ids.txt");
                var want = File.Exists(idsFile) ? File.ReadAllText(idsFile).Trim().Split(' ').ToList() : new List<string>();
                hvOk = string.Join(" ", got) == string.Join(" ", want);
                Console.WriteLine("       천계(진입값 4) 실기: " + string.Join(" ", want) + "\n       천계(진입값 4) C#:   " + string.Join(" ", got));
            }
            T.True(hvOk, "사건 메모 뒤 천계 글 순서가 실기와 다름");
            T.True(beginOk, "사건 메모 장면 시작이 실기와 다름");
            T.True(same == files.Count - 1, "하루 묶음이 실기와 다름");
            T.True(chainOk, "시작부터 이어 한 진행이 실기와 다름");
        }

        /// <summary>사건 메모 비교 범위: 세이브 영역(화면 작업표 0x0203BD20~6F · 0x0203BE00~3F · 0x0203C140~2FF(0x0203C268 날짜 칸 빼고) · 관계 슬롯표 0x0203C3E0~ 제외 — 장면 화면 초기화가 채우고 규칙은 쓰기 전에 다시 만든다),
        /// 장면 객체 규칙 칸(+0x5658~, 화면 프레임 수 +0x9948 제외), 난수 seed.</summary>
        static bool OfflineScope(uint x, uint obj)
        {
            if (x < SennenKazoku.Core.Orig.OrigMem.EwramBase + 4) return true;
            // 메모 칸 +9·+0xF 는 원작이 초기화하지 않은 지역 변수(0x080D75EC 등의 스택 구조체)에서 옮겨 적는 값이라 실기 스택 찌꺼기가 들어 있다 — 읽는 코드가 없다
            if (x >= obj + 0x56D4 && x < obj + 0x56D4 + 24 * 700 && ((x - obj - 0x56D4) % 24 == 9 || (x - obj - 0x56D4) % 24 == 15)) return false;
            if (x >= obj + 0x5658 && x < obj + SennenKazoku.Core.Orig.OrigOffline.ObjSize) return x != obj + 0x9948 && x != obj + 0x9949;
            if (x < SennenKazoku.Core.Orig.OrigMem.SaveStart || x >= SennenKazoku.Core.Orig.OrigMem.SaveEnd) return false;
            if (x >= 0x0203BD20 && x < 0x0203BD70) return false;
            if (x >= 0x0203BE00 && x < 0x0203BE40) return false;
            if (x >= 0x0203C140 && x < 0x0203C300 && (x < 0x0203C268 || x >= 0x0203C26C)) return false;
            if (x >= SennenKazoku.Core.Orig.OrigMem.Slots) return false;
            return true;
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
                    {   // 큐피트 도움말 0x0804DB50: 장면 시작 덤프의 신님 랭크·사건 인물 +0x69 로 (C# 규칙과 같은 입력)
                        uint pn = vmp.Call("0800E524", id);
                        SennenKazoku.Core.Orig.OrigEvents.CupidHelp(me, data, pn < 8 ? mp.R8(SennenKazoku.Core.Orig.OrigMem.PersonAddr((int)pn) + 0x69) : 0, mp.R16(0x0202C66E));
                    }
                    vme.Call("080111B8", 0xFFFFFFFD, id, 1, 0, v, 0, data);
                    var want = File.ReadAllBytes(sf[i + 2]);
                    var diffs = new List<string>(); int nd = 0;
                    for (uint x = 0x0202C6C4; x < SennenKazoku.Core.Orig.OrigMem.SaveEnd; x++)
                        if (me.Ewram[x - E] != want[x - E] && (x < 0x0203BC90 || x >= 0x0203BCB0) && x != 0x0203BCF0 && x != 0x0203BCF1 && x != 0x0203BAD0)
                        { nd++; if (diffs.Count < 12) diffs.Add(x.ToString("X8") + ":" + me.Ewram[x - E].ToString("X2") + "/" + want[x - E].ToString("X2")); }
                    nScenes++; if (nd == 0) sameScenes++;
                    {   // 사건을 큐에 넣는 시각(일정 명령 8, OrigGame.PushTime): 실기 작업 버퍼의 일정·사건 시각·자기 방으로 계산 → 장면 시작 시각(0x0202C682)과 비교
                        var pg2 = new SennenKazoku.Core.Orig.OrigGame(mp, rules); var own3 = new int[8]; int en = -1;
                        foreach (uint task in new uint[] { 0x0200F124, 0x02011960, 0x0201419C, 0x020169D8 })
                        {
                            uint rec = mp.R32(task + 4); int pn2 = (int)((rec - SennenKazoku.Core.Orig.OrigMem.Person) / SennenKazoku.Core.Orig.OrigMem.PersonSize);
                            if (pn2 < 0 || pn2 >= 8) continue;
                            pg2.Schedule[pn2] = mp.R32(task + 0xDB8); pg2.EventTime[pn2] = (int)mp.R32(task + 0xDCC); pg2.TodayEvent[pn2] = mp.R32(task + 0xDBC); own3[pn2] = (int)mp.R8(task + 0xDD2);
                            if (mp.R16(rec + 0x3C) == id) en = pn2;
                        }
                        int pt = en >= 0 ? pg2.PushTime(en, own3) : -1, tod = (int)mp.R16(0x0202C682);
                        if (pt >= 0 || en >= 0)
                        {
                            Console.WriteLine("       사건 넣는 시각: 인물 칸 " + en + " 계산 " + (pt / 300) + ":" + (pt % 300 / 5).ToString("00") + " · 실기 장면 " + (tod / 300) + ":" + (tod % 300 / 5).ToString("00"));
                            T.True(pt >= 0 && pt <= tod && tod - pt < 200, "사건 넣는 시각이 실기와 맞지 않음");
                        }
                    }
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
            T.Run("원작 사건 장면 자리·확대 (실기 OAM 값)", () => {
                // 인물 OBJ 왼쪽 x (기록 0x08923648 에 인물 등장 토큰을 더한 ROM 사본): 1명 104 · 2명 89,119 · 3명 76,104,132 · 4명 74..134 · 8명 34..174
                var want = new Dictionary<int, int[]> { { 1, new[] { 104 } }, { 2, new[] { 89, 119 } }, { 3, new[] { 76, 104, 132 } }, { 4, new[] { 74, 94, 114, 134 } },
                    { 5, new[] { 64, 84, 104, 124, 144 } }, { 8, new[] { 34, 54, 74, 94, 114, 134, 154, 174 } } };
                foreach (var kv in want) for (int i = 0; i < kv.Value.Length; i++) T.Eq((int)(SceneAnim.ActorCenterX(i, kv.Key) - 16), kv.Value[i]);
                // 장면 시작 밝기(실기, 액자 배경 한 점 R 값/16): 3040프레임 16 · 3052 80 · 3062 128 · 3086 255 — 화면이 검게 된 3020프레임 기준
                T.Eq((int)Math.Round((1 - SceneAnim.IntroBlack(20)) * 16), 1); T.Eq((int)Math.Round((1 - SceneAnim.IntroBlack(32)) * 16), 5);
                // 집 시간대 팔레트(원작 0x08017AF8) — 실기에서 장면 시각을 바꿔 잰 칸: 600→4, 1250·1500→6, 2420·3000·4000→0, 4820·5000→2, 5710·6500→4
                foreach (var kv in new[] { new[] { 600, 4 }, new[] { 1250, 6 }, new[] { 1500, 6 }, new[] { 2420, 0 }, new[] { 3000, 0 }, new[] { 4000, 0 }, new[] { 4820, 2 }, new[] { 5000, 2 }, new[] { 5710, 4 }, new[] { 6500, 4 } })
                    T.Eq(HouseClock.Slot(kv[0]), kv[1], "집 시간대 칸 " + kv[0]);
                T.Eq(HouseClock.FromDayFraction(0), 1800); T.Eq(HouseClock.FromDayFraction(0.75f), 0);
                T.Eq((int)Math.Round((1 - SceneAnim.IntroBlack(42)) * 16), 8); T.Eq(SceneAnim.IntroBlack(66), 0f); T.Eq(SceneAnim.IntroBlack(10), 1f);
                // 끝(마지막 장을 넘긴 뒤 t): 밝기 R 240(t20)·224(24)·208(26)·192(30)·…·16(62)·0(66) — 단계 = (255 − R)/16
                int[] ot = { 20, 24, 26, 30, 32, 36, 38, 42, 44, 48, 50, 54, 56, 60, 62, 66 };
                for (int j = 0; j < ot.Length; j++) T.Eq(SceneAnim.FadeStep(ot[j]), j + 1);
                T.Eq(SceneAnim.FadeStep(18), 0);
                // 제목 띠(실기 매 프레임, 화면이 검게 된 3020프레임 기준): 3088 −28 · 3096 +1 · 3102~3104 −10 · 3110 +1 · 3117 0(멈춤)
                T.Eq(SceneAnim.BandOffset(68), -28); T.Eq(SceneAnim.BandOffset(76), 1); T.Eq(SceneAnim.BandOffset(83), -10); T.Eq(SceneAnim.BandOffset(90), 1);
                T.Eq(SceneAnim.BandOffset(97), 0); T.Eq(SceneAnim.BandOffset(300), 0); T.True(SceneAnim.BandOffset(60) <= -28);
                // 큐피트: 3168 위 끝 13 · 3170 9 · 3172 5 · 3180 6 · 3188 7 · 3196 6 · 3204 5 (정지 그림은 5)
                int cdy; T.True(!SceneAnim.CupidOffset(147, out cdy));
                foreach (var (ct, cw) in new[] { (148, 8), (150, 4), (152, 0), (160, 1), (168, 2), (176, 1), (184, 0) }) { T.True(SceneAnim.CupidOffset(ct, out cdy)); T.Eq(cdy, cw); }
                // 확대(1A 0F 00 02): 2프레임 간격 OAM — 역배율 244·196·148·128, 두 배 크기 OBJ 가운데 (133,67)·(127,64)·(121,60)·(120,60)
                int[] pa = { 244, 196, 148, 128 }; int[,] ctr = { { 133, 67 }, { 127, 64 }, { 121, 60 }, { 120, 60 } };
                for (int j = 0; j < 4; j++)
                {
                    T.Eq((int)SceneAnim.ZoomInverse(2 * j), pa[j]);
                    SceneAnim.ZoomPlace(135, 2 * j, out float sc, out float zx, out float zy);
                    T.True(Math.Abs(zx - ctr[j, 0]) <= 1.5f && Math.Abs(zy - ctr[j, 1]) <= 1.5f, "확대 가운데 " + j + ": " + zx + "," + zy);
                }
            });
            T.Run("원작 난수기: seed×0x6D+0x3FD (ROM 0x08000614)", () => {
                var r = new Rng(1); T.Eq(r.NextU32(), 1u * 0x6Du + 0x3FDu); T.Eq(r.NextU32(), unchecked((1u * 0x6Du + 0x3FDu) * 0x6Du + 0x3FDu));
                // 부팅 씨앗 = VCOUNT × 0xAD (0x080089DC, 실기 감시: 4498 = 26×173 → 다음 값 491303 → 53553048)
                T.Eq(SennenKazoku.Core.Orig.OrigSession.BootSeed(26, 0), 4498u); T.Eq(SennenKazoku.Core.Orig.OrigSession.BootSeed(26, 1), 491303u);
                T.Eq(SennenKazoku.Core.Orig.OrigSession.BootSeed(26, 2), 53553048u);
                // 하트 표시 반 칸 수 (0x08021F14) — 실기: 128 → 1.5칸, 하트 열매 뒤 223 → 2.5칸
                var hh = new[] { 0, 1, 47, 48, 95, 96, 128, 143, 144, 191, 192, 223, 239, 240, 255 }; var want = new[] { 0, 1, 1, 2, 2, 3, 3, 3, 4, 4, 5, 5, 5, 6, 6 };
                for (int i = 0; i < hh.Length; i++) T.Eq(SennenKazoku.Core.Orig.OrigSession.HalfHearts((uint)hh[i]), want[i]);
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
            T.Run("아이템: 고리 +1000(상한 5000), 행복 상자 무드 +64 (원작 0x08024B80, 고리는 실기 확인)", () => {
                var f = NewGame.Create(1); var p = f.Members[0]; f.Items["item.ring.int"] = 2; f.Items["item.happiness_box"] = 1;
                p.Stats[0] = 2173; T.True(Interventions.Use(f, p, "item.ring.int") == null); T.Eq(p.Stats[0], 3173);
                p.Stats[0] = 4500; T.True(Interventions.Use(f, p, "item.ring.int") == null); T.Eq(p.Stats[0], 5000);
                f.Mood = 100; T.True(Interventions.Use(f, p, "item.happiness_box") == null); T.Eq(f.Mood, 164);
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
                T.Run("원작 이어 하기: 기록 → 저장·불러오기 → 1시간 뒤 사건 메모(인사·진행·보고·물음·천계·남은 날) 끝까지 (로컬)", () =>
                {
                    var rules = SennenKazoku.Core.Orig.OrigRules.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(origDir, "orig_rules.json")))));
                    rules.TreesText = File.ReadAllBytes(Path.Combine(origDir, rules.TreesFile));
                    var text = SennenKazoku.Core.Orig.OrigText.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(origDir, "orig_text.json")))));
                    SennenKazoku.Core.Orig.OrigVm.Trail = true;
                    var s0 = SennenKazoku.Core.Orig.OrigSession.NewRecommended(rules, text, "김", 0x2468);
                    var t0 = new DateTime(2026, 10, 11, 5, 39, 30);
                    s0.Record(t0); s0.PrepareSave();
                    var s = SennenKazoku.Core.Orig.OrigSession.Load(rules, text, s0.Family);
                    int d0 = s.Family.Today;
                    double hours = double.TryParse(Environment.GetEnvironmentVariable("SK_OFF_HOURS"), out var hh) ? hh : 1;
                    var prof = new Dictionary<string, long[]>(); long t1 = 0;
                    if (Environment.GetEnvironmentVariable("SK_OFF_PROF") != null)
                    {
                        s.Game.Vm.BeforeTop = (fn, a) => t1 = System.Diagnostics.Stopwatch.GetTimestamp();
                        s.Game.Vm.AfterTop = (fn, a, r) => { if (!prof.TryGetValue(fn, out var v)) prof[fn] = v = new long[2]; v[0] += System.Diagnostics.Stopwatch.GetTimestamp() - t1; v[1]++; };
                    }
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    T.True(s.StartOffline(t0.AddHours(hours)), "이어 하기 시작 안 됨");
                    var seen = new List<string>(); int steps = 0, progress = 0, heaven = 0, replayPages = 0; bool askedNo = false, viewed = false, closed = false;
                    while (s.Paused && steps++ < 50000)
                    {
                        var v = s.View();
                        if (v.EventId == "memo-progress") progress++;
                        else if (v.EventId == "heaven") heaven++;
                        else if (v.EventId != "memo" && v.EventId != "memo-progress") replayPages++;
                        else if (seen.Count < 400) seen.Add(v.Text.Replace("\n", " "));
                        if (v.NeedsChoice)
                        {   // 첫 물음은 "아니" → 메모 보기 → 첫 사건 다시 보기 → 닫기 → 다시 물음에 "응"
                            if (v.Choices.Exists(c => c.Id == "close")) { if (!viewed && v.Choices.Exists(c => c.Id == "view:0")) { viewed = true; s.Choose("view:0"); } else { closed = true; s.Choose("close"); } }
                            else if (!askedNo) { askedNo = true; s.Choose("no"); }
                            else s.Choose("yes");
                        }
                        else s.Advance();
                    }
                    if (prof.Count > 0) Console.WriteLine("       VM 호출 " + SennenKazoku.Core.Orig.OrigVm.TotalCalls + ", 정의 안 된 값 예외 " + SennenKazoku.Core.Orig.OrigVm.UndefThrows);
                    foreach (var kv in prof.OrderByDescending(kv => kv.Value[0]).Take(12)) Console.WriteLine("       시간 " + kv.Key + " " + (kv.Value[0] * 1000 / System.Diagnostics.Stopwatch.Frequency) + "ms × " + kv.Value[1]);
                    Console.WriteLine("       메모 보기: 아니 " + askedNo + ", 사건 다시 보기 장 " + replayPages + ", 닫기 " + closed);
                    if (hours == 1) T.True(askedNo && closed && (replayPages > 0 || !viewed), "메모 보기 흐름");
                    T.True(!s.Paused, "사건 메모 흐름이 끝나지 않음");
                    int days = s.Family.Today - d0;
                    Console.WriteLine("       " + hours + "시간(" + (int)(hours * 450) + "일) → " + days + "일 진행, 진행 장 " + progress + ", 천계 장 " + heaven + ", " + GameDate.Format(s.Family.Today) + ", " + sw.ElapsedMilliseconds + "ms" + (s.LineageEnded ? ", 가계 끊김" : ""));
                    foreach (var t in seen.Take(hours == 1 ? 14 : 40)) Console.WriteLine("         " + (t.Length > 110 ? t.Substring(0, 110) + "…" : t));
                    if (hours == 1) T.Eq(days, 450, "지난 날 수");
                    {   // 0일(기록 2초 뒤): 실기 글 61 → 64 68 71 74 86 (89) 75 — 진행 없이 보고, 물음 없이 끝
                        var s2 = SennenKazoku.Core.Orig.OrigSession.Load(rules, text, s0.Family);
                        T.True(s2.StartOffline(t0.AddSeconds(2)), "0일 이어 하기 시작 안 됨");
                        var ids = new List<int>(); var flow = s2.Memo;
                        int guard = 0; while (s2.Paused && guard++ < 200) { var v2 = s2.View(); if (v2.NeedsChoice) s2.Choose("yes"); else s2.Advance(); }
                        T.True(!s2.Paused, "0일 흐름이 끝나지 않음");
                        T.Eq(s2.Family.Today, d0, "0일인데 날짜가 바뀜");
                        var rep = flow.ReportIdsForTest; Console.WriteLine("       0일 보고 글: " + string.Join(" ", rep));
                        T.True(rep.Count >= 6 && rep[0] == 64 && rep[1] == 68 && rep[2] == 71 && rep[3] == 74 && rep[4] == 86 && rep[rep.Count - 1] == 75, "0일 보고 글이 실기와 다름");
                    }
                });
            if (!string.IsNullOrEmpty(origDir) && File.Exists(Path.Combine(origDir, "orig_rules.json")) && File.Exists(Path.Combine(origDir, "orig_text.json")))
                T.Run("원작 세션(앱 연결): 추천 가족 → 2년 진행 → 사건 대사 → 저장·불러오기 후 같은 진행 (로컬)", () =>
                {
                    var rules = SennenKazoku.Core.Orig.OrigRules.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(origDir, "orig_rules.json")))));
                    rules.TreesText = File.ReadAllBytes(Path.Combine(origDir, rules.TreesFile));
                    var text = SennenKazoku.Core.Orig.OrigText.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(origDir, "orig_text.json")))));
                    var s = SennenKazoku.Core.Orig.OrigSession.NewRecommended(rules, text, "김", 0x1234);
                    T.True(s.Family.Members.Count >= 2, "가족 인원 " + s.Family.Members.Count);
                    Console.WriteLine("       시작 " + GameDate.Format(s.Family.Today) + " " + string.Join(", ", s.Family.Members.ConvertAll(p => p.Name + "(" + (p.Gender == 0 ? "남" : "여") + p.Age(s.Family.Today) + "세 " + p.PlannedTitle + ")")));
                    {   // 하루 일정(0x0802292C 가 읽는 명령 표): 첫날 06:00 전(불러온 직후와 같은 경로)과 하루 진행 뒤, 칸 시간 합이 24시간(7200)·자기 방 배정
                        var hs = SennenKazoku.Core.Orig.OrigSession.NewRecommended(rules, text, "김", 0x1234);
                        var own = hs.OwnRooms();
                        for (int day = 0; day < 2; day++)
                        {
                            if (day == 1) hs.StepDay();
                            foreach (var p in hs.Family.Members)
                            {
                                var plan = hs.DayPlan(p.Id, out int evt);
                                int sum = 0; foreach (var st in plan) sum += st.End - st.Start;
                                Console.WriteLine("       일정 " + (day == 0 ? "첫날 " : "다음날 ") + p.Name + ": " + string.Join(" ", plan.ConvertAll(st => st.Kind + "→방" + st.Room + "(" + (st.End - st.Start) / 300 + "h)")) + (evt >= 0 ? "  사건 " + (evt + 1800) % 7200 / 300 + "시" : ""));
                                T.Eq(sum, 7200, p.Name + " 일정 합이 24시간이 아님");
                            }
                        }
                        {   // 큐피트 도움말: 종류 8 결과(0x08927458) · 화살 표시 비트 0 · 랭크 0→1 → 도움말 1, 다시 → 도움말 2, 랭크 1→2 → 도움말 3
                            var hm2 = hs.Game.Mem; const uint F24 = SennenKazoku.Core.Orig.OrigMem.Fam + 0x24, RK = 0x0202C66E;
                            hm2.W8(F24, 0); for (uint q = 0; q < 3; q++) hm2.W8(0x0203BD30 + q, 0xFF);
                            uint rk = hm2.R16(RK); hm2.W16(RK, 1);
                            SennenKazoku.Core.Orig.OrigEvents.CupidHelp(hm2, 0x08927458, 1, 0);
                            SennenKazoku.Core.Orig.OrigEvents.CupidHelp(hm2, 0x08927458, 1, 0);
                            hm2.W16(RK, 2); SennenKazoku.Core.Orig.OrigEvents.CupidHelp(hm2, 0x08927458, 0, 1);
                            var got = new List<int>(); for (int h = SennenKazoku.Core.Orig.OrigEvents.PopHelp(hm2); h >= 0; h = SennenKazoku.Core.Orig.OrigEvents.PopHelp(hm2)) got.Add(h);
                            var hv = new SennenKazoku.Core.Orig.OrigHeaven(hs.Game.Vm, text, text.Heaven, x => "이름", "신님");
                            var hp1 = hv.HelpPages(1);
                            Console.WriteLine("       큐피트 도움말 차례 " + string.Join(",", got) + ", 가족 +0x24 = 0x" + hm2.R8(F24).ToString("X") + ", 도움말 1 첫 장: " + (hp1.Count > 0 ? hp1[0].Text.Replace("\n", " / ") : "(없음)") + " (" + hp1.Count + "장)");
                            T.Eq(string.Join(",", got), "1,2,3", "큐피트 도움말 차례");
                            T.True(hp1.Count > 0 && hp1[0].Text.Contains("화살"), "큐피트 도움말 1 글");
                            hm2.W16(RK, rk); hm2.W8(F24, 0);
                        }
                        Console.WriteLine("       자기 방(인물 칸 0~7): " + string.Join(",", own) + ", 원작 요일 값(0x08095C18) " + GameDate.Format(hs.Family.Today) + " → " + hs.Game.Weekday());
                    }
                    {   // 천계 보고(결혼식 뒤 원작 0x0809F938 진입값 2 → 5 → 6): 시작일을 100년 전으로, 세대 기록 수(+0x6A3)를 2(첫 세대교체)로 두면
                        // 가계도 설명 → 작명서 → 칭호·유형 평 → 통신 결혼의 화살 → 세기 확인(1세기 = 새 시대) → 시대 연출 → 새 시대 축하·시간의 책갈피·기록의 전당 → 마무리.
                        // 몸통 세트 0 → 1, 가족 직업이 새 시대 것으로, 시간의 책갈피 +1, 통신 결혼의 화살(화살 칸 10) 1, 고인 목록 비움.
                        var cs = SennenKazoku.Core.Orig.OrigSession.NewRecommended(rules, text, "김", 0x1234); var cmm = cs.Game.Mem;
                        SennenKazoku.Core.Orig.OrigDate.Get(cmm, SennenKazoku.Core.Orig.OrigMem.Date, out int ty, out int tmo, out int tdd);
                        SennenKazoku.Core.Orig.OrigDate.Set(cmm, 0x0202C688, ty - 100, tmo, tdd); cmm.W8(0x0202C6B3, 2);
                        var jobs0 = string.Join(",", cs.Family.Members.ConvertAll(p => p.Job)); uint bm0 = cmm.R8(0x0202C65A);
                        cs.HeavenReport(); cs.Project();
                        var hv = cs.LastHeaven;
                        int era1 = (int)cmm.R8(0x0202C692); var jobs1 = string.Join(",", cs.Family.Members.ConvertAll(p => p.Job)); uint bm1 = cmm.R8(0x0202C65A);
                        Console.WriteLine("       천계 상태 차례: " + string.Join(" ", hv.Trace.ConvertAll(x => x.ToString("X"))));
                        Console.WriteLine("       천계 글 번호: " + string.Join(" ", hv.Pages.ConvertAll(x => x.MsgId < 0 ? "[" + x.Screen + "]" : (x.Speaker == 2 ? "큐" : x.Speaker == 1 ? "신" : "?") + x.MsgId)));
                        foreach (var pg in hv.Pages.Take(6)) Console.WriteLine("         " + (pg.Speaker == 1 ? "높으신 신" : pg.Speaker == 2 ? "큐피트" : "화면") + ": " + pg.Text.Replace("\n", " / "));
                        Console.WriteLine("       새 시대 직업 이름: " + string.Join(", ", cs.Family.Members.ConvertAll(p => SennenKazoku.Core.Orig.OrigJobs.Name(p.Job))));
                        Console.WriteLine("       천계 보고(100년째 첫 세대교체): 몸통 세트 " + era1 + ", 직업 " + jobs0 + " → " + jobs1 + ", 시간의 책갈피 " + bm0 + " → " + bm1 +
                            ", 통신 결혼의 화살 " + cmm.R8(0x0202C64A) + ", 가족 +0x20 = 0x" + cmm.R32(0x0202C030).ToString("X") + ", 화면 세트 " + cs.Family.BodySet);
                        T.True(era1 == 1 && cs.Family.BodySet == 1 && jobs0 != jobs1 && bm1 == bm0 + 1, "천계 보고: 새 시대·책갈피");
                        T.True(cmm.R8(0x0202C64A) == 1 && cmm.R16(0x02038F80) == 0xFFFF && (cmm.R32(0x0202C030) & 0x810) == 0x810, "천계 보고: 통신 결혼의 화살·고인 목록·표지");
                        // 진입값 2: 0~4 (2대째 처음 → 3 가계도 설명) · 진입값 5: 고인 없음 → 0x25, 2대째 → 통신 결혼의 화살, 1세기 = 새 시대(0x37) → 0x38 · 진입값 6: 0x39~0x44(첫 새 시대) → 0x45 → 0x47(진입값 2)
                        string want = "0 1 2 3 4 5 6 7 8 9 A B 25 26 27 28 29 2A 2B 2C 2D 35 37 38 39 3A 3B 3C 3D 3E 3F 40 41 42 43 44 45 47 48";
                        T.Eq(string.Join(" ", hv.Trace.ConvertAll(x => x.ToString("X"))), want, "천계 상태 차례");
                        if (text.Heaven.Ok) T.True(hv.Pages.Exists(x => x.MsgId == 371) && hv.Pages.Exists(x => x.MsgId == 400) && hv.Pages.Exists(x => x.MsgId == 403) && hv.Pages.Exists(x => x.MsgId == 481) && hv.Pages.Exists(x => x.MsgId == 435) && hv.Pages.Exists(x => x.MsgId == 260), "천계 대사");
                    }
                    {   // 선대의 꿈의 화살: 꿈 목록(0x0202C4B4)에 첫 인물의 꿈 하나를 넣고 화살 칸 8 = 1 → 원작 메뉴 경로(칸 100+k)로 쏘면
                        // 대상 관심사(+0x3E)가 바뀌고 목록이 비며 칸 8 이 0 이 돼야 한다
                        var ds = SennenKazoku.Core.Orig.OrigSession.NewRecommended(rules, text, "김", 0x1234); var dm = ds.Game.Mem;
                        var dwho = ds.Family.Members[ds.Family.Members.Count - 1]; uint ddrec = 0;
                        for (int n = 0; n < 8; n++) if ((int)dm.R16(SennenKazoku.Core.Orig.OrigMem.PersonAddr(n) + 0x3C) == dwho.Id) ddrec = SennenKazoku.Core.Orig.OrigMem.PersonAddr(n);
                        uint dream = 0x30;   // 관심사 번호(특별 기록 표) — 시험용
                        dm.W16(0x0202C4B4, (uint)ds.Family.Members[0].Id); dm.W16(0x0202C4B6, dream); dm.W16(0x0202C4B8, 0xFFFF); dm.W8(0x0202C648, 1);
                        uint i0 = dm.R16(ddrec + 0x3E);
                        var derr = ds.UseLegacyDream(dwho, 0);
                        Console.WriteLine("       선대의 꿈의 화살(" + ds.DreamName((int)dream) + " → " + dwho.Name + "): " + (derr ?? "성공") + ", 관심사 " + i0 + " → " + dm.R16(ddrec + 0x3E) +
                            ", 목록 " + ds.LegacyDreams().Count + ", 칸 8 " + dm.R8(0x0202C648));
                        T.True(derr == null && ds.LegacyDreams().Count == 0 && dm.R8(0x0202C648) == 0 && dm.R16(ddrec + 0x3E) != i0, "선대의 꿈의 화살");
                    }
                    var wdump = Environment.GetEnvironmentVariable("SK_WEDDING_DUMP");
                    if (!string.IsNullOrEmpty(wdump))
                    {   // 실기 확인용(로컬): 천계 보고가 처음 도는 날(결혼식)까지 진행하고, 그 며칠 전의 저장 영역(0x0202C010~)을 파일로 남긴다.
                        // 결혼식은 날짜로 예약되는 사건이라 이 상태를 실기 저장 상태에 심으면(capture memload) 원작이 같은 날 결혼식 → 천계 보고를 돈다.
                        var ws = SennenKazoku.Core.Orig.OrigSession.NewRecommended(rules, text, "김", 0x4321); var wm = ws.Game.Mem;
                        var ring = new Queue<byte[]>(); var dates = new Queue<string>();
                        for (int d = 0; d < 365 * 80 && !ws.LineageEnded; d++)
                        {
                            ring.Enqueue(wm.SaveBlock()); dates.Enqueue(GameDate.Format(ws.Family.Today)); if (ring.Count > 4) { ring.Dequeue(); dates.Dequeue(); }
                            var hv0 = ws.LastHeaven;
                            ws.StepDay(); while (ws.Paused) { ws.View(); ws.Advance(); }
                            if (ws.LastHeaven != hv0)
                            {
                                Directory.CreateDirectory(wdump);
                                File.WriteAllBytes(Path.Combine(wdump, "pre_wedding.bin"), ring.Peek());
                                File.WriteAllLines(Path.Combine(wdump, "pre_wedding.txt"), new[] { "저장 영역 날짜 " + dates.Peek() + " (다음 날부터 진행)", "결혼식 날 " + GameDate.Format(ws.Family.Today),
                                    "천계 상태 " + string.Join(" ", ws.LastHeaven.Trace.ConvertAll(x => x.ToString("X"))),
                                    "천계 글 " + string.Join(",", ws.LastHeaven.Pages.ConvertAll(x => x.MsgId)) });
                                foreach (var pg in ws.LastHeaven.Pages) File.AppendAllText(Path.Combine(wdump, "pre_wedding.txt"), (pg.Speaker == 1 ? "신: " : pg.Speaker == 2 ? "큐: " : "화면: ") + pg.Text.Replace("\n", " / ") + "\n");
                                Console.WriteLine("       결혼식 전 상태 저장: " + dates.Peek() + " → 결혼식 " + GameDate.Format(ws.Family.Today));
                                break;
                            }
                        }
                    }
                    if (Environment.GetEnvironmentVariable("SK_LONG") != null)
                    {   // 장기(환경변수 SK_LONG): 300년 동안 세대 기록 수·몸통 세트·시간의 책갈피 변화
                        var ls = SennenKazoku.Core.Orig.OrigSession.NewRecommended(rules, text, "김", 0x4321); var lm = ls.Game.Mem;
                        uint g0 = lm.R8(0x0202C6B3), e0 = lm.R8(0x0202C692); var log = new List<string>();
                        for (int d = 0; d < 365 * 300 && !ls.LineageEnded; d++)
                        {
                            var hv0 = ls.LastHeaven;
                            ls.StepDay(); while (ls.Paused) { ls.View(); ls.Advance(); }
                            if (ls.LastHeaven != hv0 && ls.LastHeaven != null)
                                log.Add(GameDate.Format(ls.Family.Today) + " 천계[" + string.Join(" ", ls.LastHeaven.Trace.ConvertAll(x => x.ToString("X"))) + "] 글 " +
                                    string.Join(",", ls.LastHeaven.Pages.ConvertAll(x => x.MsgId).Distinct()));
                            uint g = lm.R8(0x0202C6B3), e = lm.R8(0x0202C692);
                            if (g != g0 || e != e0) { log.Add(GameDate.Format(ls.Family.Today) + " 세대기록 " + g + " 세트 " + e + " 책갈피 " + lm.R8(0x0202C65A)); g0 = g; e0 = e; }
                        }
                        Console.WriteLine("       300년: " + string.Join(" / ", log) + (ls.LineageEnded ? " (가문 끊김)" : ""));
                    }
                    // 결과 예고: 원작 메모리를 바꾸지 않아야 한다
                    var mem0 = Convert.ToBase64String(s.Game.Mem.SaveBlock()); var seed0 = s.Game.Mem.R32(0x02000000);
                    foreach (var p in s.Family.Members)
                    {
                        var hi = s.Predict(p.Id, true); var lo = s.Predict(p.Id, false);
                        Console.WriteLine("       예고 " + p.Name + " 255: " + (hi == null ? "-" : hi.Title + " " + EffectChange.Format(s.Family, hi.Changes)) + " / 0: " + (lo == null ? "-" : lo.Title + " " + EffectChange.Format(s.Family, lo.Changes)));
                    }
                    T.True(mem0 == Convert.ToBase64String(s.Game.Mem.SaveBlock()) && seed0 == s.Game.Mem.R32(0x02000000), "결과 예고가 원작 메모리를 바꿨다");
                    // 화살·아이템: 원작 함수(0x0801CECC·0x08024440·0x08024B80)로 쓰고 다음 날에도 원작 메모리에 남는지
                    // SK_REC_DIR 이 있으면 이 호출들의 앞뒤 메모리를 남긴다 (romlift/replay_check.py 로 원작 ROM 과 비교)
                    var toolRec = Environment.GetEnvironmentVariable("SK_REC_DIR");
                    if (!string.IsNullOrEmpty(toolRec))
                    {
                        Directory.CreateDirectory(toolRec); int tn = 0; byte[] pre = null; var tm = s.Game.Mem;
                        byte[] TSnap() { var b = new byte[0x40000 + 0x8000 + 0x40000 + 0x400]; Array.Copy(tm.Ewram, 0, b, 0, 0x40000); Array.Copy(tm.Iwram, 0, b, 0x40000, 0x8000); Array.Copy(tm.Frames, 0, b, 0x48000, 0x40000); Array.Copy(tm.Io, 0, b, 0x88000, 0x400); return b; }
                        s.Game.Vm.BeforeTop = (fn, a) => { pre = TSnap(); };
                        s.Game.Vm.AfterTop = (fn, a, r) =>
                        {
                            using (var w = new BinaryWriter(File.Create(Path.Combine(toolRec, "tool_" + (tn++).ToString("D3") + ".bin"))))
                            { w.Write(0x31434552u); w.Write(uint.Parse(fn, System.Globalization.NumberStyles.HexNumber)); w.Write(0u); w.Write((uint)a.Length); foreach (var x in a) w.Write(x); w.Write(r); w.Write(pre); w.Write(TSnap()); }
                        };
                    }
                    var who = s.Family.Members[0]; int s0 = who.Stats[0]; var mm = s.Game.Mem;
                    uint whoRec = 0; for (int k = 0; k < 8; k++) if (SennenKazoku.Core.Orig.OrigGame.Present(mm, k) && (int)mm.R16(SennenKazoku.Core.Orig.OrigMem.PersonAddr(k) + 0x3C) == who.Id) whoRec = SennenKazoku.Core.Orig.OrigMem.PersonAddr(k);
                    T.True(Interventions.Count(s.Family, "arrow.encourage") == 5, "시작 화살 5개");
                    var enc = s.Use(who, "arrow.encourage"); T.True(enc == null, "힘내라 화살: " + enc); T.Eq((int)mm.R8(whoRec + 0x69) & 7, 3);
                    var again = s.Use(who, "arrow.calm"); T.True(again != null && again.StartsWith("화살의 효과가"), "효과 중 거절: " + again);
                    foreach (var t in Interventions.Tools) if (t.OrigSlot >= 0 && s.CanUse(t.Id) && t.Id != "arrow.encourage" && t.Id != "arrow.calm") s.Family.Items[t.Id] = 2;
                    int h0 = (int)mm.R8(whoRec + 0x5B); s.Family.Items["item.poison_heart_fruit"] = 1;
                    T.True(s.Use(who, "item.heart_fruit") == null, "하트 열매"); T.Eq((int)mm.R8(whoRec + 0x5B), Math.Min(255, h0 + 95));
                    T.True(s.Use(who, "item.poison_heart_fruit") == null, "독 하트 열매"); T.Eq((int)mm.R8(whoRec + 0x5B), 0);
                    T.Eq(Interventions.Count(s.Family, "item.poison_heart_fruit"), 0);
                    T.True(s.Use(who, "item.poison_heart_fruit") != null, "보유 0 이면 거부");
                    int mood0 = s.Family.Mood; T.True(s.Use(who, "item.happiness_box") == null, "행복 상자"); T.Eq(s.Family.Mood, Math.Min(255, mood0 + 64));
                    T.True(s.Use(who, "item.ring.int") == null, "지력의 고리"); T.Eq(s.Family.Get(who.Id).Stats[0], Math.Min(5000, s0 + 1000));
                    var torch = s.Use(who, "item.torch"); Console.WriteLine("       횃불(악마 없음): " + torch); T.True(torch != null, "악마 없으면 횃불 거부");
                    Console.WriteLine("       사랑의 고리: " + (s.Use(who, "item.ring.love") ?? "씀") + " / 왕관: " + (s.Use(who, "item.crown") ?? "씀"));
                    var kid = s.Family.Members.Find(p => p.FatherId == s.Family.HeadId || p.MotherId == s.Family.HeadId);
                    if (kid != null) Console.WriteLine("       왕관(세대주 자녀 " + kid.Name + "): " + (s.Use(kid, "item.crown") ?? "씀") + ", 후계자 0x0202C67E=" + mm.R16(0x0202C67E));
                    // 선대 마음의 결정 — 실기와 같은 상황: 목록 (0, 스킬 5)·(1, 스킬 9), 결정 1개 → 고른 사람이 스킬 5, 목록 한 칸 당겨짐, 결정 0
                    {
                        const uint LL = SennenKazoku.Core.Orig.OrigSession.LegacyList;
                        mm.W16(LL, 0); mm.W8(LL + 2, 5); mm.W8(LL + 3, 0xFF); mm.W16(LL + 4, 1); mm.W8(LL + 6, 9); mm.W8(LL + 7, 0xFF); mm.W16(LL + 8, 0xFFFF);
                        for (uint k = 0; k < 3; k++) mm.W8(whoRec + 0x62 + k, 0xFF);
                        s.Family.Items["item.heart_crystal"] = 1;
                        var lr = s.UseLegacyHeart(s.Family.Get(who.Id), 0);
                        T.True(lr == null, "선대 마음의 결정: " + lr);
                        T.Eq((int)mm.R8(whoRec + 0x62), 5); T.Eq(s.LegacyHearts().Count, 1); T.Eq(s.LegacyHearts()[0][1], 9);
                        T.Eq(Interventions.Count(s.Family, "item.heart_crystal"), 0);
                        Console.WriteLine("       선대 마음의 결정: 스킬 " + string.Join(",", s.Family.Get(who.Id).Skills.ConvertAll(k => k + " " + s.SkillName(k))) + ", 남은 마음 " + s.LegacyHearts().Count + ", 스킬 이름 " + s.Text.Skills.Count + "개");
                        if (s.Text.HasCharset) T.True(s.Text.Skills.Count == 183 && !s.SkillName(12).Contains("번"), "스킬 이름 표");
                        if (s.Text.Skills.Count > 12 && s.Text.Skills[12].Desc.Length > 0)
                        {
                            Console.WriteLine("       스킬 12: " + s.SkillName(12) + " — " + s.SkillDesc(12) + " / 스킬 5: " + s.SkillName(5) + " — " + s.SkillDesc(5).Replace("\n", " "));
                            T.True(s.Text.Skills.TrueForAll(x => !SennenKazoku.Core.Orig.OrigText.Untranslated(x.Name + x.Desc + x.Effect)), "번역 안 된 스킬 글");
                        }
                    }
                    foreach (var t in Interventions.Tools)
                        if (t.Kind == "arrow" && t.OrigSlot >= 2 && s.CanUse(t.Id))
                        {
                            var q = s.Family.Members[s.Family.Members.Count - 1]; uint qa = 0;
                            for (int k = 0; k < 8; k++) if (SennenKazoku.Core.Orig.OrigGame.Present(mm, k) && (int)mm.R16(SennenKazoku.Core.Orig.OrigMem.PersonAddr(k) + 0x3C) == q.Id) qa = SennenKazoku.Core.Orig.OrigMem.PersonAddr(k);
                            var before = (byte[])mm.Ewram.Clone();
                            var r = s.Use(q, t.Id); int diff = 0; for (int i = 0; i < before.Length; i++) if (before[i] != mm.Ewram[i]) diff++;
                            int hitNow = s.Family.Get(q.Id).HitArrow;
                            Console.WriteLine("       " + t.Name + " → " + q.Name + ": " + (r ?? "씀") + " (바뀐 바이트 " + diff + ", 맞은 화살 칸 " + hitNow + ")");
                            if (r == null && t.OrigSlot <= 5) T.Eq(hitNow, t.OrigSlot);
                        }
                    // 시간의 책갈피: 쓰면 책갈피 표시(0x0202C678 & 0x80), 능력치를 바꾼 뒤 되돌리면 쓴 때 값으로
                    {
                        s.Family.Items["item.bookmark"] = 1;
                        var bw = s.Family.Get(who.Id); int st0 = bw.Stats[1];
                        var br = s.Use(bw, "item.bookmark");
                        T.True(br == null && s.HasBookmark, "시간의 책갈피 쓰기: " + br);
                        mm.W16(whoRec + 0x52, 1234); s.Project();
                        bool rb = s.RestoreBookmark();
                        Console.WriteLine("       시간의 책갈피: 되돌리기 " + rb + ", 체력 " + st0 + " → 1234 → " + s.Family.Get(who.Id).Stats[1]);
                        T.True(rb && s.Family.Get(who.Id).Stats[1] == st0, "책갈피로 되돌아가지 않음");
                    }
                    // 가문이 끊김: 책갈피를 쓴 뒤 가장을 원작 죽음 처리(0x08114658)로 죽이면 결과 첫 단어 2 → 끊김, 책갈피로 되돌리면 이어진다
                    {
                        s.Family.Items["item.bookmark"] = 1; T.True(s.Use(s.Family.Get(who.Id), "item.bookmark") == null, "책갈피 쓰기");
                        int head = s.Family.HeadId; T.True(!s.LineageEnded, "시작에는 가문이 이어짐");
                        mm.W32(0x0203C440, 0); s.Game.Vm.Call("slots", (uint)head, 0); s.Game.Vm.Call("08114658", 0, 0x0203C440); s.Project();
                        Console.WriteLine("       가장 " + head + " 죽음 → 결과 " + mm.R32(0x0203C440) + ", 가문이 끊김 " + s.LineageEnded + ", 가족 " + s.Family.Members.Count + "명");
                        T.True(mm.R32(0x0203C440) == 2 && s.LineageEnded, "가장이 죽으면 가문이 끊겨야 함");
                        var recs = s.FamilyRecords();
                        Console.WriteLine("       가문의 기록: " + string.Join(" / ", recs.ConvertAll(r => r.Rank + "위 " + r.Years + "년 가족 " + r.StartY + "." + r.StartM + "." + r.StartD + "~" + r.EndY + "." + r.EndM + "." + r.EndD + " 역대 " + r.Members + "명 " + s.FamilyTypeName(r.Type))));
                        T.True(recs.Count == 1 && recs[0].Members > 0, "가문의 기록 한 칸");
                        var curRec = s.CurrentRecord();
                        T.True(curRec != null && curRec.Rank == 1, "지금 가족의 기록 찾기");
                        Console.WriteLine("       기록의 원작 가문 이름 칸: " + SennenKazoku.Core.Orig.OrigSession.RecordOrigName(s.Text, curRec) + ", 열쇠 " + SennenKazoku.Core.Orig.OrigSession.RecordKey(curRec));
                        {   // 제목 화면 새로 시작(0x0800D778 방식 1·2)이 가문의 기록을 남기는가
                            var m2 = new SennenKazoku.Core.Orig.OrigMem(); Array.Copy(mm.Ewram, m2.Ewram, mm.Ewram.Length); Array.Copy(mm.Iwram, m2.Iwram, mm.Iwram.Length);
                            var vm2 = rules.CreateVm(m2);
                            uint mode = SennenKazoku.Core.Orig.OrigNewGame.TitleResetMode(m2);
                            SennenKazoku.Core.Orig.OrigNewGame.TitleNewGame(vm2);
                            var after = SennenKazoku.Core.Orig.OrigSession.ReadRecords(vm2);
                            Console.WriteLine("       제목 화면 새로 시작(방식 " + mode + ") 뒤 가문의 기록 " + after.Count + "칸 (정상 세이브는 전원을 켤 때 초기화하지 않음)");
                            T.True(after.Count == 1, "새로 시작해도 가문의 기록이 남아야 함");
                            s.PrepareSave();
                            var ns = SennenKazoku.Core.Orig.OrigSession.NewRecommended(rules, s.Text, "", 12345, cartridge: SennenKazoku.Core.Orig.OrigSession.CartridgeOf(s.Family));
                            T.True(ns.FamilyRecords().Count == 1 && ns.Family.Members.Count > 0 && !ns.LineageEnded, "이전 카트리지에서 새 가족");
                            Console.WriteLine("       이전 카트리지에서 새 가족: " + ns.Family.Members.Count + "명, 가문의 기록 " + ns.FamilyRecords().Count + "칸, 감사의 마음 " + ns.Game.Mem.R32(0x0202C670) + ", 랭크 " + ns.Game.Mem.R16(0x0202C66E));
                        }
                        T.True(s.RestoreBookmark() && !s.LineageEnded && s.Family.Get(head) != null, "책갈피로 되돌아가 가문이 이어져야 함");
                    }
                    s.Game.Vm.BeforeTop = null; s.Game.Vm.AfterTop = null;
                    s.StepDay(); while (s.Paused) s.Advance();
                    var who2 = s.Family.Get(who.Id);
                    Console.WriteLine("       개입: 지력 " + s0 + " → 고리 → 다음 날 " + who2.Stats[0] + ", 힘내라 화살 남은 수 " + Interventions.Count(s.Family, "arrow.encourage") + ", 화살 표시 " + who2.ArrowFlags);
                    T.True(who2.Stats[0] >= Math.Min(Stat.Max, s0 + 1000) - 50 && Interventions.Count(s.Family, "arrow.encourage") == 4, "개입이 원작 메모리에 남지 않음");
                    int shown = 0, withText = 0, pagesTotal = 0; string sample = null;
                    string grat = null, unkSample = null, rankPage = null, sceneSample = null; int unknownGlyph = 0, appSummary = 0, sceneN = 0;
                    void Drain(SennenKazoku.Core.Orig.OrigSession ss)
                    {
                        while (ss.Paused)
                        {
                            var v = ss.View(); pagesTotal++;
                            if (ss == s && sample == null && v.TextSource != "none" && v.Text.Length > 20) sample = v.Title + " / " + v.Text.Replace("\n", " ");
                            if (ss == s && v.Scene != null) { sceneN++; if (sceneSample == null && v.Scene.Actors.Count >= 2) sceneSample = v.Title + ": " + v.Scene.Pic + " " + v.Scene.Band + " " + v.Scene.Back + " 인물 " + string.Join(",", v.Scene.Actors.ConvertAll(a => a.PersonId + "/" + a.Anim)); }
                            if (ss == s && v.Text.Contains("□")) { unknownGlyph++; if (unkSample == null || unkSample.Length < 1500) unkSample += " ‖ " + v.Text.Replace("\n", " "); }
                            if (ss == s && grat == null && v.Text.Contains("감사의 마음")) grat = v.Text.Replace("\n", " ");
                            if (ss == s && v.Text.Contains("천계에서 도착") && rankPage == null) rankPage = v.Text.Replace("\n", " ");
                            if (ss == s && v.Text.Contains("[앱 요약]")) appSummary++;
                            if (ss.Advance()) { shown++; if (v.TextSource != "none") withText++; }
                        }
                    }
                    for (int d = 0; d < 730; d++) { s.StepDay(); Drain(s); }
                    Console.WriteLine("       2년: 보여 준 사건 " + shown + "개 (대사 있음 " + withText + "), 장 " + pagesTotal + ", " + GameDate.Format(s.Family.Today));
                    Console.WriteLine("       예: " + sample);
                    Console.WriteLine("       원작 결과 문구 예: " + (grat ?? "없음") + " / 글자표에 없는 글자가 든 장 " + unknownGlyph);
                    if (unknownGlyph > 0) Console.WriteLine("       글자표에 없는 코드: " + string.Join(" ", s.Text.Missing.Select(c => c.ToString("X4"))) + " / 예: " + unkSample);
                    if (s.Text.HasCharset) T.Eq(unknownGlyph, 0, "글자표에 없는 글자");
                    {   // 실기 캡처한 사건(0x08923648 "마음의 친구여"): 친구(가족 아님)·사건 인물 둘, 첫 장은 둘 다 동작 2(♪)
                        SennenKazoku.Core.Orig.OrigText.Rec r0;
                        if (s.Text.Records.TryGetValue(0x08923648, out r0))
                        {
                            var st = new List<List<SennenKazoku.Core.Orig.OrigText.StageActor>>();
                            var endSt = new List<SennenKazoku.Core.Orig.OrigText.StageActor>();
                            var pg = SennenKazoku.Core.Orig.OrigText.ScenePages(r0.Script, a => "○", st, endSt);
                            string first = string.Join(",", st[0].ConvertAll(a => a.Slot + "/" + a.Anim));
                            Console.WriteLine("       장면 토큰(0x08923648): 장 " + pg.Count + ", 첫 장 인물 " + first + ", 마지막 장 " + string.Join(",", st[st.Count - 1].ConvertAll(a => a.Slot + "/" + a.Anim)));
                            T.True(st.Count == pg.Count && first == "-1/2,0/2", "장면 토큰 해석 (실기: 왼쪽 친구·오른쪽 사건 인물, ♪)");
                            // 1A 0F 00 02 → 사건 인물 확대(실기: 마지막 대사 뒤 "잇세이「헤헤♪」" 장), 00 01 → 결과 문구 앞에서 원래대로
                            var zoomed = new List<int>(); for (int z = 0; z < st.Count; z++) if (st[z].Exists(a => a.Zoom)) zoomed.Add(z);
                            Console.WriteLine("       확대 장: " + string.Join(",", zoomed) + " / " + pg.Count + " — " + (zoomed.Count > 0 ? pg[zoomed[0]].Replace("\n", " ") : "없음"));
                            T.True(zoomed.Count == 1 && st[zoomed[0]].Find(a => a.Zoom).Slot == 0 && zoomed[0] == pg.Count - 1 && !endSt.Exists(a => a.Zoom), "1A 0F 확대 장(마지막 대사 장), 결과 문구 앞에서 풀림");

                        }
                    }
                    Console.WriteLine("       장면 그림이 있는 장 " + sceneN + " / " + pagesTotal + ", 예: " + (sceneSample ?? "없음"));
                    T.True(sceneN > pagesTotal / 2, "장면 그림 정보");
                    Console.WriteLine("       랭크 보상 설명(대체 글 " + s.Text.Overrides.Count + "개): " + (rankPage ?? "없음") + " / 앱 요약 장 " + appSummary);
                    if (s.Text.Overrides.Count > 0) T.True(rankPage != null && appSummary == 0, "랭크 보상 설명을 대체 글로 보여 줘야 함");
                    Console.WriteLine("       2년 뒤 보유: " + string.Join(", ", Interventions.Tools.FindAll(t => Interventions.Count(s.Family, t.Id) > 0).ConvertAll(t => t.Name + "×" + Interventions.Count(s.Family, t.Id)))
                        + " / 신님에게 감사 " + string.Join(", ", s.Family.Members.ConvertAll(p => p.Name + " " + p.Gratitude))
                        + " / 감사의 마음 " + s.Game.Mem.R32(0x0202C670) + ", 신님 랭크 " + s.Game.Mem.R16(0x0202C66E));
                    T.True(shown > 100 && withText * 10 >= shown * 9, "사건 수·대사 비율");
                    // 저장 → 불러오기 → 두 세션을 60일 같이 진행: 같은 사건이 나야 한다 (아직 안 보여 준 그날 사건은 다 보여 주고 저장)
                    while (s.HasPending) { s.StepDay(); while (s.Paused) { s.View(); s.Advance(); } }
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
                    int fd = 0; while (fd < a.Count && fd < b.Count && a[fd] == b[fd]) fd++;
                    T.True(a.Count > 0 && string.Join("|", a) == string.Join("|", b), "불러온 뒤 진행이 다름 " + a.Count + "/" + b.Count + " — 처음 다른 곳 " + fd + ": " + (fd < a.Count ? a[fd] : "-") + " / " + (fd < b.Count ? b[fd] : "-"));
                    T.True(Convert.ToBase64String(s.Game.Mem.SaveBlock()) == Convert.ToBase64String(s2.Game.Mem.SaveBlock()), "60일 뒤 원작 메모리가 다름");
                    Console.WriteLine("       저장·불러오기 뒤 60일 사건 " + a.Count + "개 같음, 원작 메모리 같음");
                });
            if (!string.IsNullOrEmpty(origDir) && File.Exists(Path.Combine(origDir, "custom_create.ram")))
                T.Run("원작 내가 아는 가족: 입력값 → 설정 블록이 실기(가족 레코드 만들기 직전)와 같음 (로컬)", () =>
                {
                    var rules = SennenKazoku.Core.Orig.OrigRules.FromJson(J.Obj(MiniJson.Parse(File.ReadAllText(Path.Combine(origDir, "orig_rules.json")))));
                    rules.TreesText = File.ReadAllBytes(Path.Combine(origDir, rules.TreesFile));
                    var want = File.ReadAllBytes(Path.Combine(origDir, "custom_create.ram"));
                    var m = new SennenKazoku.Core.Orig.OrigMem(); var vm = rules.CreateVm(m);
                    SennenKazoku.Core.Orig.OrigNewGame.BlankCartridge(vm); SennenKazoku.Core.Orig.OrigNewGame.TitleNewGame(vm);
                    // 실기 입력: 아버지·어머니 1986-01-01 A형 내향적 능력 순위 지·체·매·운 직업 첫 번째 체격 첫 번째, 아들 2004-01-01
                    uint S0 = SennenKazoku.Core.Orig.OrigNewGame.Setup, M0 = SennenKazoku.Core.Orig.OrigNewGame.Members, MS = SennenKazoku.Core.Orig.OrigNewGame.MemberSize;
                    byte[] Bytes(uint a, int n) { var b = new byte[n]; Array.Copy(want, (int)(a - 0x02000000), b, 0, n); return b; }
                    var ms = new List<SennenKazoku.Core.Orig.OrigNewGame.CustomMember>();
                    int[] slots = { 2, 3, 4 }; int[] years = { 1986, 1986, 2004 };
                    for (int k = 0; k < 3; k++)
                        ms.Add(new SennenKazoku.Core.Orig.OrigNewGame.CustomMember { Slot = slots[k], Year = years[k], Name = Bytes(M0 + MS * (uint)k + 0x0E, 14), Look = Bytes(M0 + MS * (uint)k + 0x30, 16) });
                    SennenKazoku.Core.Orig.OrigNewGame.Custom(vm, 2005, 1, 1, ms, false);
                    int diff = 0; var sb = new System.Text.StringBuilder();
                    void Cmp(uint a, int n, string tag)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            uint got = m.R8(a + (uint)i), w = want[a - 0x02000000 + i];
                            if (got != w) { diff++; if (diff <= 30) sb.Append(" " + tag + "+" + i.ToString("X") + ":" + w.ToString("X2") + "/" + got.ToString("X2")); }
                        }
                    }
                    Cmp(S0, 0x44, "머리");
                    for (int k = 0; k < 3; k++) Cmp(M0 + MS * (uint)k, (int)MS, "m" + k);
                    Console.WriteLine("       설정 블록 다른 바이트 " + diff + " (실기/C#)" + sb);
                    T.True(diff == 0, "설정 블록이 실기와 다름");
                    // 앱 입력(FamilySetup) → OrigSession.NewCustom 이 같은 사람들(생일·성별·직업·능력치)을 만드는지: 실기 덤프 위 원작 만들기 결과와 비교
                    var fsu = new FamilySetup { StartDay = GameDate.Make(2005, 1, 1), Surname = "김" };
                    var fa = fsu.Add(FamilyRole.Father); fa.Name = "민준"; fa.BirthDay = GameDate.Make(1986, 1, 1); fa.Blood = "A"; fa.Personality = 0; fa.Job = 21;
                    var mo = fsu.Add(FamilyRole.Mother); mo.Name = "지우"; mo.BirthDay = GameDate.Make(1986, 1, 1); mo.Blood = "A"; mo.Personality = 0; mo.Job = 21;
                    var so = fsu.Add(FamilyRole.Child, 0); so.Name = "서준"; so.BirthDay = GameDate.Make(2004, 1, 1); so.Blood = "A"; so.Personality = 0; so.Job = 1;
                    Console.WriteLine("       직업 후보(아버지): " + string.Join(", ", SennenKazoku.Core.Orig.OrigJobs.Candidates(rules, fsu.StartDay, fa.BirthDay, 0, 2).ConvertAll(SennenKazoku.Core.Orig.OrigJobs.Name)));
                    var cs = SennenKazoku.Core.Orig.OrigSession.NewCustom(rules, null, fsu, 0x1234);
                    var mref = new SennenKazoku.Core.Orig.OrigMem(); Array.Copy(want, mref.Ewram, 0x40000);
                    rules.CreateVm(mref).Call("080417E0", S0);
                    int pd = 0;
                    for (int n = 0; n < 3; n++)
                    {
                        uint a = SennenKazoku.Core.Orig.OrigMem.PersonAddr(n);
                        // 능력치(+0x50~)는 만들기 때 난수가 섞인다(난수 상태가 실기 진행과 달라 다름) — 정해지는 값만 비교
                        foreach (uint off in new uint[] { 0x2E, 0x2F, 0x30, 0x31, 0x3C, 0x58, 0x59, 0x5A, 0x5B })
                            if (cs.Game.Mem.R8(a + off) != mref.R8(a + off)) { pd++; Console.WriteLine("         레코드" + n + " +" + off.ToString("X") + " 원작 " + mref.R8(a + off) + " / 앱 " + cs.Game.Mem.R8(a + off)); }
                    }
                    Console.WriteLine("       앱 입력 → 원작 경로: " + string.Join(", ", cs.Family.Members.ConvertAll(p => p.Name + " " + SennenKazoku.Core.Orig.OrigJobs.Name(p.Job) + " 능력 " + string.Join("/", p.Stats))) + " — 실기 만들기와 다른 바이트 " + pd);
                    T.True(pd == 0, "앱 입력으로 만든 가족이 실기와 다름");
                    // 가족 레코드 만들기: 실기 덤프(만들기 직전) 위에서 C# 로 0x080417E0 을 돌려 저장 영역을 남긴다 → romlift 로 원작 실행과 비교
                    var dump = Environment.GetEnvironmentVariable("SK_CUSTOM_OUT");
                    if (!string.IsNullOrEmpty(dump))
                    {
                        var m2 = new SennenKazoku.Core.Orig.OrigMem(); Array.Copy(want, m2.Ewram, 0x40000);
                        var vm2 = rules.CreateVm(m2); vm2.Call("080417E0", S0);
                        File.WriteAllBytes(dump, m2.Ewram);
                    }
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
                        if (f != last) { SennenKazoku.Core.Orig.OrigDate.Get(m, SennenKazoku.Core.Orig.OrigMem.Date, out int ey, out int em, out int ed); Console.WriteLine("       " + ey + "-" + em + "-" + ed + " 가족: " + f + " (가장 " + m.R16(0x0202C67C) + ")"); last = f; }
                    }
                    Console.WriteLine("       " + days + "일, 사건 " + nev + " (종류별 " + string.Join(",", kinds.Select(kv => kv.Key + ":" + kv.Value)) + "), " + sw.ElapsedMilliseconds + "ms");
                    Console.WriteLine("       해마다 사건: " + string.Join(" ", perYear));
                    Console.WriteLine("       보통 복귀가 아닌 장면 끝(종류@결과 기록): " + (g.EndCodes.Count == 0 ? "없음" : string.Join(" ", g.EndCodes.Take(30))) + ", 감사의 마음 " + m.R32(0x0202C670) + ", 신님 랭크 " + m.R16(0x0202C66E));
                });
            var daysDir = Environment.GetEnvironmentVariable("SK_DAYS_DIR");
            if (!string.IsNullOrEmpty(origDir) && !string.IsNullOrEmpty(daysDir) && Directory.Exists(daysDir))
                T.Run("원작 하루 진행 비교: 날 바뀜 덤프 → C# 하루 → 다음 날 바뀜 덤프 (로컬 자료 있을 때만)", () => OrigDays(origDir, daysDir));
            var offDir = Environment.GetEnvironmentVariable("SK_OFFLINE_DIR");
            if (!string.IsNullOrEmpty(origDir) && !string.IsNullOrEmpty(offDir) && Directory.Exists(offDir))
                T.Run("원작 전원 꺼진 동안 진행 비교: 사건 메모 하루 묶음(0x080CB74C) 덤프 → C# → 다음 덤프 (로컬 자료 있을 때만)", () => OrigOffline(origDir, offDir));

            Console.WriteLine("\n통과 " + T.Pass + " / 실패 " + T.Fail);
            return T.Fail == 0 ? 0 : 1;
        }
    }
}

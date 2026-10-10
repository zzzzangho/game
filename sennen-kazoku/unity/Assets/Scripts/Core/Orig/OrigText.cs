using System;
using System.Collections.Generic;
using System.Text;

namespace SennenKazoku.Core.Orig
{
    /// <summary>
    /// 원작 결과 기록 → 사건 제목·대사 (로컬 팩 orig_text.json, tools/romlift/export_text.py 가 만든다. 번역 대사이므로 저장소에 넣지 않는다).
    /// 대사의 제어 토큰은 원작 글 명령(0x1A xx ...)이다. 지금 해석하는 것:
    ///   1A 01 줄바꿈 · 1A 02 글상자 끝 · 1A 09 버튼 대기(다음 장) · 1A 06 aa bb 이름 자리 (aa &lt; 28 이면 장면 슬롯표 aa 의 인물, 0x27 가문 이름).
    /// 나머지(색 1A 03, 표정·연출 1A 0E/0F/10, 말하는 사람 1A 08 등)는 화면 연출이라 글에서 뺀다.
    /// 이름 자리의 뜻은 대사 문맥으로 추정한 것이다(0x26 등 모르는 자리는 "○○"로 둔다).
    /// </summary>
    public sealed class OrigText
    {
        public sealed class Rec { public string Id = "", Title = "", Script = ""; }
        public readonly Dictionary<uint, Rec> Records = new Dictionary<uint, Rec>();
        public readonly Dictionary<long, string> Interests = new Dictionary<long, string>();
        /// <summary>
        /// 번역 패치 글자표 (orig_text.json "charset", tools/romlift/export_charset.py): 글 코드(1바이트 &lt; 0x80, 2바이트 0x81xx~) → 글자.
        /// 한글은 0x889F 부터 차례로, 0x8740~0x8743 은 받침에 따라 고르는 조사(은/는·이/가·을/를·과/와), 나머지는 Shift-JIS 기호.
        /// </summary>
        public readonly Dictionary<int, string> Charset = new Dictionary<int, string>();
        public bool HasCharset { get { return Charset.Count > 0; } }
        /// <summary>
        /// 스킬 (스킬 표 0x088A309C — 스킬 번호 = 레코드 +0x62~0x64 값). orig_text.json "skills":
        /// export_charset.py 는 패치 이름(글자열)만, export_skills.py 는 원문에서 옮긴 한국어 {name, desc, effect} 를 넣는다.
        /// </summary>
        public sealed class Skill { public string Name = "", Desc = "", Effect = ""; }
        public readonly List<Skill> Skills = new List<Skill>();
        /// <summary>
        /// 번역 패치가 옮기지 않은 원문 글을 대신하는 한국어 (orig_text.json "overrides": 글 주소 → 글, 원문에서 직접 옮긴 로컬 번역).
        /// 글 안의 "|" 다음 장, "/" 줄바꿈, {플레이어}·{가문} 이름, {이가}·{은는}·{을를}·{과와} 앞 이름 받침에 맞춘 조사.
        /// </summary>
        public readonly Dictionary<uint, string> Overrides = new Dictionary<uint, string>();
        /// <summary>화면 글 표 0x085C081C (원작 코드가 0x085C081C + 4·번호로 고른다 — 가족 유형 349~357, 순위 381~385 등). orig_text.json "ui".</summary>
        public readonly List<string> Ui = new List<string>();
        public string UiText(int k, string fallback) { return k >= 0 && k < Ui.Count && Ui[k].Length > 0 ? Ui[k] : fallback; }
        /// <summary>Decode 에서 만난, 글자표에 없는 코드 (점검용).</summary>
        public readonly HashSet<int> Missing = new HashSet<int>();

        public static OrigText FromJson(Dictionary<string, object> d)
        {
            var t = new OrigText();
            if (d == null) return t;
            var r = J.Child(d, "records");
            if (r != null)
                foreach (var kv in r)
                {
                    var l = kv.Value as List<object>; if (l == null || l.Count < 3) continue;
                    t.Records[Convert.ToUInt32(kv.Key.Substring(2), 16)] = new Rec { Id = (string)l[0], Title = (string)l[1], Script = (string)l[2] };
                }
            var it = J.Child(d, "interests");
            if (it != null)
                foreach (var kv in it)
                {
                    var p = kv.Key.Split(','); if (p.Length != 2) continue;
                    t.Interests[OrigRules.Key(int.Parse(p[0]), int.Parse(p[1]))] = kv.Value as string ?? "";
                }
            var cs = J.Child(d, "charset");
            if (cs != null) foreach (var kv in cs) t.Charset[Convert.ToInt32(kv.Key, 16)] = kv.Value as string ?? "";
            var ui = d.ContainsKey("ui") ? d["ui"] as List<object> : null;
            if (ui != null) foreach (var x in ui) t.Ui.Add(x as string ?? "");
            var ov = J.Child(d, "overrides");
            if (ov != null) foreach (var kv in ov) t.Overrides[Convert.ToUInt32(kv.Key.Substring(2), 16)] = kv.Value as string ?? "";
            var sk = d.ContainsKey("skills") ? d["skills"] as List<object> : null;
            if (sk != null)
                foreach (var x in sk)
                {
                    var o = x as Dictionary<string, object>;
                    if (o != null) t.Skills.Add(new Skill { Name = J.Str(o, "name", ""), Desc = J.Str(o, "desc", ""), Effect = J.Str(o, "effect", "") });
                    else t.Skills.Add(new Skill { Name = x as string ?? "" });
                }
            return t;
        }

        /// <summary>ROM 의 글(0 또는 1A FF 에서 끝)을 바이트로 읽는다 — 글자 해독은 Decode.</summary>
        public static List<byte> ReadRaw(OrigMem m, uint addr, int max = 2048)
        {
            var r = new List<byte>();
            for (int i = 0; i < max; i++)
            {
                byte b = (byte)m.R8(addr + (uint)i);
                if (b == 0 || (b == 0x1A && m.R8(addr + (uint)i + 1) == 0xFF)) break;
                r.Add(b);
            }
            return r;
        }

        /// <summary>
        /// 원작 글 바이트를 글자표로 풀어 장(글상자 단위) 목록으로 만든다.
        /// 1A 01 줄바꿈 · 1A 02 / 1A 09 다음 장 · 1A 06 aa 이름 자리(name(aa), 0x27 가문 이름) · 1A 86 lo hi 결과 스크립트의 이름 표지(person(족보 번호 / 0x3FFD 가문 / 0x3FFE 플레이어)) ·
        /// 1A 05 숫자 자리(결과 스크립트는 원작 함수로 이미 글로 바꿔 둔다 — 남아 있으면 "?") · 나머지 토큰(색·표정·말하는 사람)은 화면 연출이라 뺀다. 글자표에 없는 코드는 "□".
        /// </summary>
        public List<string> Decode(IList<byte> b, Func<int, string> name, Func<int, string> person)
        {
            var pages = new List<string>(); var sb = new StringBuilder();
            void Flush() { var s = FixJosa(sb.ToString()).Trim(); if (s.Length > 0) pages.Add(s); sb.Clear(); }
            int i = 0;
            while (i < b.Count)
            {
                int c = b[i];
                if (c == 0) { i++; continue; }
                if (c == 0x1A && i + 1 < b.Count)
                {
                    int op = b[i + 1];
                    if (op == 0xFF) break;
                    if (op == 0x01) sb.Append('\n');
                    else if (op == 0x02 || op == 0x09) Flush();
                    else if (op == 0x06 && i + 2 < b.Count) sb.Append((name != null ? name(b[i + 2]) : null) ?? "○○");
                    else if (op == 0x86 && i + 3 < b.Count) sb.Append((person != null ? person(OrigResultScript.MarkId(b[i + 2], b[i + 3])) : null) ?? "○○");
                    else if (op == 0x05) sb.Append('?');
                    else if (op == 0x87 && i + 5 < b.Count)
                    {
                        uint addr = (uint)(b[i + 2] | b[i + 3] << 8 | b[i + 4] << 16 | b[i + 5] << 24);
                        string ov; if (Overrides.TryGetValue(addr, out ov)) AppendOverride(sb, ov, person, Flush);
                    }
                    int len = op == 0x86 ? 4 : op == 0x87 ? 6 : TokenLen(b, i);
                    i += len > 0 ? len : 2;
                    continue;
                }
                int code = c;
                if (c >= 0x80 && i + 1 < b.Count) { code = c << 8 | b[i + 1]; i += 2; } else i++;
                string ch; if (Charset.TryGetValue(code, out ch)) sb.Append(ch); else { sb.Append("□"); Missing.Add(code); }
            }
            Flush();
            return pages;
        }

        static void AppendOverride(StringBuilder sb, string text, Func<int, string> person, Action flush)
        {
            string P(uint id) { return (person != null ? person((int)id) : null) ?? "○○"; }
            text = text.Replace("{플레이어}", P(OrigResultScript.MarkPlayer)).Replace("{가문}", P(OrigResultScript.MarkFamily))
                       .Replace("{이가}", "이/가").Replace("{은는}", "은/는").Replace("{을를}", "을/를").Replace("{과와}", "과/와");
            var pages = text.Split('|');
            for (int k = 0; k < pages.Length; k++)
            {
                if (k > 0) flush();
                sb.Append(pages[k].Replace('/', '\n'));
            }
            flush();
        }

        /// <summary>
        /// 번역 패치가 옮기지 않은 일본어 장인가 — 가나(히라가나·가타카나)가 들어 있으면 그렇다.
        /// 이런 글(랭크 보상 설명 일부)은 한자 자리가 패치 글꼴의 한글로 바뀌어 실기에서도 뜻이 통하지 않는다.
        /// </summary>
        public static bool Untranslated(string page)
        {
            foreach (var ch in page) if (ch >= '\u3041' && ch <= '\u30FA') return true;
            return false;
        }

        /// <summary>글 토큰 1A xx 의 길이 (OrigResultScript.TokenLen 과 같은 표, 모르면 -1).</summary>
        static int TokenLen(IList<byte> b, int i)
        {
            switch (b[i + 1])
            {
                case 0x01: case 0x02: case 0x09: case 0x0D: case 0xFF: return 2;
                case 0x03: case 0x0A: return 3;
                case 0x05: case 0x06: case 0x08: case 0x0B: case 0x0F: return 4;
                case 0x10: return 5;
                case 0x0E: return SceneLen(b, i);
                default: return -1;
            }
        }

        public string InterestTitle(int table, int index) { return Interests.TryGetValue(OrigRules.Key(table, index), out var s) ? s : ""; }

        /// <summary>대사를 장(글상자 단위)으로 나눠 이름 자리를 채운다. name(aa) 가 null 이면 "○○".</summary>
        public static List<string> Pages(string script, Func<int, string> name) { return ScenePages(script, name, null, null); }

        /// <summary>장면 인물: Slot = 슬롯표 칸(1A 0E 01 의 첫 인자, 가족), −1 = 가족이 아닌 사람(1A 0E 04). Anim = 동작 번호.</summary>
        public sealed class StageActor { public int Slot = -1; public int Anim = -1; public int NpcKind = -1, NpcAge = -1, Outfit = 0xFF; public string NpcKey = ""; public bool Mirror, Back, Zoom; }

        /// <summary>
        /// 대사를 장으로 나누면서 장마다 액자 안 인물 상태를 같이 낸다(stages[k] = k 번째 장을 보일 때의 상태).
        /// 장면 토큰(원작 글 엔진이 장면 우편함 [T+0x398] 에 넘기는 명령 0x0E, 0x080AAC44): 1A 0E 00 a(1) · 1A 0E 01 (칸, 반전, 뒷모습, 옷) 가족 인물 등장 ·
        /// 1A 0E 02 (동작, 인물 순번) · 1A 0E 03 a · 1A 0E 04 (계열, 나이대, ?, ?, 옷) 가족이 아닌 사람 등장. 인물 순번은 등장한 순서
        /// (실기 캡처: 기록 0x08923648 은 04 → 01 순서로 왼쪽 친구, 오른쪽 사건 인물). 동작 번호 뜻은 실기 캡처로 확인(0~0x14 감정 말풍선).
        /// 참고 번역문은 토큰을 바이트 단위로 쪼개 적기도 해서({{HEX:1A 0E}}{{HEX:04}}…) 이어진 HEX 묶음을 하나의 바이트열로 읽는다.
        /// </summary>
        public static List<string> ScenePages(string script, Func<int, string> name, List<List<StageActor>> stages, List<StageActor> actorsOut)
        {
            var pages = new List<string>(); var sb = new StringBuilder();
            var actors = new List<StageActor>(); var pageStart = new List<StageActor>();
            List<StageActor> Snap() { var l = new List<StageActor>(); foreach (var a in actors) l.Add(new StageActor { Slot = a.Slot, Anim = a.Anim, NpcKind = a.NpcKind, NpcAge = a.NpcAge, Outfit = a.Outfit, NpcKey = a.NpcKey, Mirror = a.Mirror, Back = a.Back, Zoom = a.Zoom }); return l; }
            void Flush() { var s = FixJosa(sb.ToString()).Trim(); if (s.Length > 0) { pages.Add(s); if (stages != null) stages.Add(pageStart); } sb.Clear(); pageStart = Snap(); }
            var bytes = new List<byte>();
            void Bytes()
            {
                int k = 0;
                while (k < bytes.Count)
                {
                    if (bytes[k] != 0x1A || k + 1 >= bytes.Count) { k++; continue; }
                    int op = bytes[k + 1];
                    if (op == 0x12 || op == 0xFF) { k = bytes.Count; break; }
                    int len = op == 0x0E ? SceneLen(bytes, k) : TokenLen(bytes, k);
                    if (len < 0) len = 2;
                    int Arg(int j) { return k + j < bytes.Count ? bytes[k + j] : 0; }
                    if (op == 0x01) sb.Append('\n');
                    else if (op == 0x02 || op == 0x09) Flush();
                    else if (op == 0x06) sb.Append(name(Arg(2)) ?? "○○");
                    else if (op == 0x0E)
                    {
                        int sub = Arg(2);
                        bool fresh = sb.ToString().Trim().Length == 0;   // 장 글이 시작하기 전이면 이 장의 처음 상태에 넣는다
                        if (sub == 1) actors.Add(new StageActor { Slot = Arg(3), Mirror = (Arg(4) & 1) != 0, Back = Arg(5) == 1, Outfit = Arg(6) });
                        else if (sub == 4) actors.Add(new StageActor { Slot = -1, NpcKind = Arg(3), NpcAge = Arg(4), Outfit = Arg(7),
                            NpcKey = Arg(3).ToString("X2") + Arg(4).ToString("X2") + Arg(5).ToString("X2") + Arg(6).ToString("X2") + Arg(7).ToString("X2") });
                        else if (sub == 2 && Arg(4) < actors.Count) actors[Arg(4)].Anim = Arg(3);
                        if (fresh) pageStart = Snap();
                    }
                    else if (op == 0x0F)
                    {
                        // 장면 명령 0xF (0x080AAD50): 1A 0F 칸 02 = 그 칸 인물 확대(2배, SceneAnim.ZoomPlace) · 01 = 원래대로.
                        // ROM 대사는 거의 모두 마지막 대사 뒤 "00 02"(사건 인물의 한마디) → 결과 문구 앞 "00 01" 로 쓴다(실기 캡처 확인).
                        bool fresh = sb.ToString().Trim().Length == 0;
                        foreach (var a in actors) if (a.Slot == Arg(2)) { if (Arg(3) == 2) a.Zoom = true; else if (Arg(3) == 1) a.Zoom = false; }
                        if (fresh) pageStart = Snap();
                    }
                    k += len;
                }
                bytes.Clear();
            }
            int i = 0;
            while (i < script.Length)
            {
                int a = script.IndexOf("{{HEX:", i, StringComparison.Ordinal);
                string text = a < 0 ? script.Substring(i) : script.Substring(i, a - i);
                if (text.Length > 0) { Bytes(); sb.Append(text); }
                if (a < 0) break;
                int b = script.IndexOf("}}", a, StringComparison.Ordinal);
                if (b < 0) break;
                foreach (var h in script.Substring(a + 6, b - a - 6).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) bytes.Add(Convert.ToByte(h, 16));
                i = b + 2;
            }
            Bytes();
            Flush();
            if (actorsOut != null) actorsOut.AddRange(Snap());
            return pages;
        }

        /// <summary>장면 토큰 1A 0E 의 길이 (하위 명령별 인자 수: 0·3 → 1, 1 → 4, 2 → 2, 4 → 5, 5 → 0 — 0x080AAC44 의 하위 함수와 ROM 대사로 확인).</summary>
        public static int SceneLen(int sub)
        {
            switch (sub) { case 0: case 3: return 4; case 1: return 7; case 2: return 5; case 4: return 8; default: return 3; }
        }
        static int SceneLen(IList<byte> b, int i) { return SceneLen(i + 2 < b.Count ? b[i + 2] : -1); }

        /// <summary>번역문이 "은/는"처럼 둘 다 적은 조사를 앞 글자 받침에 맞게 하나로 고른다.</summary>
        public static string FixJosa(string s)
        {
            string[][] pairs = { new[] { "은/는", "은는" }, new[] { "이/가", "이가" }, new[] { "을/를", "을를" }, new[] { "과/와", "과와" }, new[] { "와/과", "과와" }, new[] { "(으)로", "으로" } };
            foreach (var p in pairs)
            {
                int k;
                while ((k = s.IndexOf(p[0], StringComparison.Ordinal)) >= 0)
                {
                    string prev = k > 0 ? s.Substring(k - 1, 1) : "";
                    s = s.Substring(0, k) + Template.Josa(prev, p[1]) + s.Substring(k + p[0].Length);
                }
            }
            return s;
        }
    }
}

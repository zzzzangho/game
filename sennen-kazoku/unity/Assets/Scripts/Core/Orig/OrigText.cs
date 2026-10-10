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
                    int len = op == 0x86 ? 4 : TokenLen(b, i);
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
                case 0x0E: { int sub = i + 2 < b.Count ? b[i + 2] : -1; return sub == 0 ? 4 : sub == 1 ? 7 : sub == 2 ? 5 : 2; }
                default: return -1;
            }
        }

        public string InterestTitle(int table, int index) { return Interests.TryGetValue(OrigRules.Key(table, index), out var s) ? s : ""; }

        /// <summary>대사를 장(글상자 단위)으로 나눠 이름 자리를 채운다. name(aa) 가 null 이면 "○○".</summary>
        public static List<string> Pages(string script, Func<int, string> name)
        {
            var pages = new List<string>(); var sb = new StringBuilder();
            void Flush() { var s = FixJosa(sb.ToString()).Trim(); if (s.Length > 0) pages.Add(s); sb.Clear(); }
            int i = 0;
            while (i < script.Length)
            {
                int a = script.IndexOf("{{HEX:", i, StringComparison.Ordinal);
                if (a < 0) { sb.Append(script, i, script.Length - i); break; }
                sb.Append(script, i, a - i);
                int b = script.IndexOf("}}", a, StringComparison.Ordinal);
                if (b < 0) break;
                var hex = script.Substring(a + 6, b - a - 6).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                i = b + 2;
                if (hex.Length < 2 || hex[0] != "1A") continue;
                int op = Convert.ToInt32(hex[1], 16);
                if (op == 0x01) sb.Append('\n');
                else if (op == 0x02 || op == 0x09) Flush();
                else if (op == 0x06 && hex.Length >= 3) sb.Append(name(Convert.ToInt32(hex[2], 16)) ?? "○○");
            }
            Flush();
            return pages;
        }

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

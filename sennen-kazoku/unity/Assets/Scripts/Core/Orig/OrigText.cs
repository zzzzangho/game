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
            return t;
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

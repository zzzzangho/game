using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SennenKazoku.Core
{
    /// <summary>의존성 없는 최소 JSON 파서/작성기. 객체=Dictionary, 배열=List, 정수=long, 실수=double.</summary>
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            var p = new Parser(s);
            p.Ws();
            var v = p.Value();
            p.Ws();
            if (p.i != s.Length) throw new FormatException("JSON 끝에 불필요한 문자 (위치 " + p.i + ")");
            return v;
        }

        public static string Serialize(object o, bool pretty = false)
        {
            var sb = new StringBuilder();
            Write(sb, o, pretty, 0);
            return sb.ToString();
        }

        sealed class Parser
        {
            readonly string s; public int i;
            public Parser(string s) { this.s = s; }
            public void Ws() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
            Exception Err(string m) { return new FormatException(m + " (위치 " + i + ")"); }
            public object Value()
            {
                if (i >= s.Length) throw Err("예상치 못한 끝");
                char c = s[i];
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '"') return Str();
                if (c == 't' && Lit("true")) return true;
                if (c == 'f' && Lit("false")) return false;
                if (c == 'n' && Lit("null")) return null;
                return Num();
            }
            bool Lit(string w)
            {
                if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) throw Err("잘못된 값");
                i += w.Length; return true;
            }
            object Num()
            {
                int st = i;
                if (i < s.Length && s[i] == '-') i++;
                bool fl = false;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c >= '0' && c <= '9') i++;
                    else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { fl = true; i++; }
                    else break;
                }
                if (st == i) throw Err("숫자 아님");
                string t = s.Substring(st, i - st);
                if (!fl && long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
                throw Err("숫자 형식 오류");
            }
            string Str()
            {
                i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw Err("문자열이 닫히지 않음");
                    char c = s[i++];
                    if (c == '"') break;
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw Err("잘못된 이스케이프");
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Err("잘못된 \\u");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                return sb.ToString();
            }
            List<object> Arr()
            {
                i++; var l = new List<object>(); Ws();
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    Ws(); l.Add(Value()); Ws();
                    if (i >= s.Length) throw Err("배열이 닫히지 않음");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw Err("배열 구분자 오류");
                }
            }
            Dictionary<string, object> Obj()
            {
                i++; var d = new Dictionary<string, object>(); Ws();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws();
                    if (i >= s.Length || s[i] != '"') throw Err("객체 키 필요");
                    string k = Str(); Ws();
                    if (i >= s.Length || s[i] != ':') throw Err("':' 필요");
                    i++; Ws(); d[k] = Value(); Ws();
                    if (i >= s.Length) throw Err("객체가 닫히지 않음");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Err("객체 구분자 오류");
                }
            }
        }

        static void Indent(StringBuilder sb, bool pretty, int n) { if (pretty) { sb.Append('\n'); sb.Append(' ', n * 2); } }

        static void Write(StringBuilder sb, object o, bool pretty, int lvl)
        {
            switch (o)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: WriteStr(sb, s); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> m:
                    sb.Append('{'); bool first = true;
                    foreach (var kv in m)
                    {
                        if (!first) sb.Append(','); first = false;
                        Indent(sb, pretty, lvl + 1); WriteStr(sb, kv.Key); sb.Append(pretty ? ": " : ":");
                        Write(sb, kv.Value, pretty, lvl + 1);
                    }
                    if (!first) Indent(sb, pretty, lvl);
                    sb.Append('}'); break;
                case System.Collections.IEnumerable e:
                    sb.Append('['); bool f1 = true;
                    foreach (var x in e)
                    {
                        if (!f1) sb.Append(','); f1 = false;
                        Indent(sb, pretty, lvl + 1); Write(sb, x, pretty, lvl + 1);
                    }
                    if (!f1) Indent(sb, pretty, lvl);
                    sb.Append(']'); break;
                default: throw new ArgumentException("직렬화 불가 형식: " + o.GetType());
            }
        }

        static void WriteStr(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>JSON 트리 접근 도우미.</summary>
    public static class J
    {
        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }
        public static List<object> Arr(object o) { return o as List<object>; }
        public static object Get(Dictionary<string, object> d, string k)
        { object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
        public static string Str(Dictionary<string, object> d, string k, string def = "")
        { var v = Get(d, k) as string; return v ?? def; }
        public static long Long(Dictionary<string, object> d, string k, long def = 0)
        {
            var v = Get(d, k);
            if (v is long l) return l;
            if (v is double x) return (long)x;
            return def;
        }
        public static int Int(Dictionary<string, object> d, string k, int def = 0) { return (int)Long(d, k, def); }
        public static bool Bool(Dictionary<string, object> d, string k, bool def = false)
        { var v = Get(d, k); return v is bool b ? b : def; }
        public static List<object> List(Dictionary<string, object> d, string k)
        { return Get(d, k) as List<object> ?? new List<object>(); }
        public static Dictionary<string, object> Child(Dictionary<string, object> d, string k)
        { return Get(d, k) as Dictionary<string, object>; }
    }
}

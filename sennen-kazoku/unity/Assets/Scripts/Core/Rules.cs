using System;
using System.Collections.Generic;

namespace SennenKazoku.Core
{
    /// <summary>앱이 이해하는 조건·효과 목록. 여기에 없는 op를 쓰는 콘텐츠는 앱 업데이트가 필요하다.</summary>
    public static class Capabilities
    {
        /// <summary>데이터 계약 버전. 새 op/변수를 추가하면 올린다.</summary>
        public const int Contract = 1;
        public static readonly HashSet<string> CondOps = new HashSet<string> {
            "always", "and", "or", "not", "cmp", "chance", "has_skill", "flag", "has_role" };
        public static readonly HashSet<string> ExprOps = new HashSet<string> { "add", "sub", "mul", "div" };
        public static readonly HashSet<string> EffectOps = new HashSet<string> {
            "add_stat", "add_hearts", "add_immersion", "add_job_mastery", "set_job", "add_skill", "remove_skill",
            "family_mood", "family_assets", "set_flag", "clear_flag", "set_planned_state", "log" };
        public static readonly HashSet<string> Roles = new HashSet<string> { "self", "spouse", "father", "mother" };
        public static readonly HashSet<string> PersonVars = new HashSet<string> {
            "age", "int", "stamina", "charm", "luck", "hearts", "immersion", "job", "mastery", "gender", "married" };
        public static readonly HashSet<string> FamilyVars = new HashSet<string> { "mood", "mood_level", "assets", "house" };
        public static readonly HashSet<string> DateVars = new HashSet<string> { "year", "month", "day" };
        public static readonly HashSet<string> Cmps = new HashSet<string> { "<", "<=", ">", ">=", "==", "!=" };
    }

    public sealed class RuleContext
    {
        public Family Family;
        public Rng Rng;
        public Dictionary<string, Person> Roles = new Dictionary<string, Person>();
        public Person Self { get { Person p; return Roles.TryGetValue("self", out p) ? p : null; } }
    }

    public static class Rules
    {
        // ---------- 검증 ----------
        public static void ValidateCondition(object c, string path, List<string> errors)
        {
            var d = J.Obj(c);
            if (d == null) { errors.Add(path + ": 조건은 객체여야 함"); return; }
            string op = J.Str(d, "op");
            if (!Capabilities.CondOps.Contains(op)) { errors.Add(path + ": 지원하지 않는 조건 op '" + op + "' (앱 업데이트 필요)"); return; }
            switch (op)
            {
                case "and": case "or":
                    var args = J.List(d, "args");
                    if (args.Count == 0) errors.Add(path + ": args 비어 있음");
                    for (int i = 0; i < args.Count; i++) ValidateCondition(args[i], path + ".args[" + i + "]", errors);
                    break;
                case "not": ValidateCondition(J.Get(d, "arg"), path + ".arg", errors); break;
                case "cmp":
                    if (!Capabilities.Cmps.Contains(J.Str(d, "cmp"))) errors.Add(path + ": 잘못된 비교자 '" + J.Str(d, "cmp") + "'");
                    ValidateExpr(J.Get(d, "l"), path + ".l", errors); ValidateExpr(J.Get(d, "r"), path + ".r", errors); break;
                case "chance":
                    if (J.Int(d, "den") <= 0 || J.Int(d, "num") < 0) errors.Add(path + ": chance num/den 오류"); break;
                case "has_skill": case "flag": case "has_role": break;
            }
            if (op == "has_role" || op == "has_skill" || op == "flag")
            {
                string role = J.Str(d, "role", "self");
                if (!Capabilities.Roles.Contains(role) && !(op == "flag" && J.Str(d, "scope") == "family"))
                    errors.Add(path + ": 알 수 없는 역할 '" + role + "'");
            }
        }

        public static void ValidateExpr(object e, string path, List<string> errors)
        {
            if (e is long || e is double) return;
            var s = e as string;
            if (s != null)
            {
                var parts = s.Split('.');
                bool ok = parts.Length == 2 &&
                    ((Capabilities.Roles.Contains(parts[0]) && Capabilities.PersonVars.Contains(parts[1])) ||
                     (parts[0] == "family" && Capabilities.FamilyVars.Contains(parts[1])) ||
                     (parts[0] == "date" && Capabilities.DateVars.Contains(parts[1])));
                if (!ok) errors.Add(path + ": 알 수 없는 변수 '" + s + "'");
                return;
            }
            var d = J.Obj(e);
            if (d == null || d.Count != 1) { errors.Add(path + ": 수식 형식 오류"); return; }
            foreach (var kv in d)
            {
                if (!Capabilities.ExprOps.Contains(kv.Key)) { errors.Add(path + ": 지원하지 않는 수식 '" + kv.Key + "'"); return; }
                var a = kv.Value as List<object>;
                if (a == null || a.Count != 2) { errors.Add(path + ": 수식 인자는 2개"); return; }
                ValidateExpr(a[0], path + "." + kv.Key + "[0]", errors); ValidateExpr(a[1], path + "." + kv.Key + "[1]", errors);
            }
        }

        public static void ValidateEffects(List<object> effects, string path, List<string> errors)
        {
            for (int i = 0; i < effects.Count; i++)
            {
                var d = J.Obj(effects[i]); string p = path + "[" + i + "]";
                if (d == null) { errors.Add(p + ": 효과는 객체여야 함"); continue; }
                string op = J.Str(d, "op");
                if (!Capabilities.EffectOps.Contains(op)) { errors.Add(p + ": 지원하지 않는 효과 op '" + op + "' (앱 업데이트 필요)"); continue; }
                string target = J.Str(d, "target", "self");
                bool personOp = op != "family_mood" && op != "family_assets" && op != "log" && !(op == "set_flag" || op == "clear_flag") ;
                if (personOp && !Capabilities.Roles.Contains(target)) errors.Add(p + ": 알 수 없는 target '" + target + "'");
                if (op == "add_stat" && Stat.FromKey(J.Str(d, "stat")) < 0) errors.Add(p + ": 알 수 없는 stat '" + J.Str(d, "stat") + "'");
            }
        }

        // ---------- 평가 ----------
        public static bool Eval(object cond, RuleContext cx)
        {
            if (cond == null) return true;
            var d = J.Obj(cond);
            switch (J.Str(d, "op"))
            {
                case "always": return true;
                case "and": foreach (var a in J.List(d, "args")) if (!Eval(a, cx)) return false; return true;
                case "or": foreach (var a in J.List(d, "args")) if (Eval(a, cx)) return true; return false;
                case "not": return !Eval(J.Get(d, "arg"), cx);
                case "cmp":
                    double l, r;
                    if (!Num(J.Get(d, "l"), cx, out l) || !Num(J.Get(d, "r"), cx, out r)) return false;
                    switch (J.Str(d, "cmp"))
                    {
                        case "<": return l < r; case "<=": return l <= r; case ">": return l > r;
                        case ">=": return l >= r; case "==": return l == r; case "!=": return l != r;
                    }
                    return false;
                case "chance": return cx.Rng.Chance(J.Int(d, "num"), J.Int(d, "den"));
                case "has_role": return cx.Roles.ContainsKey(J.Str(d, "role", "self"));
                case "has_skill":
                    Person p; return cx.Roles.TryGetValue(J.Str(d, "role", "self"), out p) && p.Skills.Contains(J.Int(d, "id"));
                case "flag":
                    if (J.Str(d, "scope") == "family") return cx.Family.Flags.Contains(J.Str(d, "name"));
                    Person q; return cx.Roles.TryGetValue(J.Str(d, "role", "self"), out q) && q.Flags.Contains(J.Str(d, "name"));
            }
            return false;
        }

        /// <summary>수식 계산. 없는 역할을 참조하면 false (조건은 불충족 처리).</summary>
        public static bool Num(object e, RuleContext cx, out double v)
        {
            v = 0;
            if (e is long) { v = (long)e; return true; }
            if (e is double) { v = (double)e; return true; }
            var s = e as string;
            if (s != null) return Var(s, cx, out v);
            var d = J.Obj(e);
            if (d == null) return false;
            foreach (var kv in d)
            {
                var a = kv.Value as List<object>; double x, y;
                if (a == null || a.Count != 2 || !Num(a[0], cx, out x) || !Num(a[1], cx, out y)) return false;
                switch (kv.Key)
                {
                    case "add": v = x + y; return true;
                    case "sub": v = x - y; return true;
                    case "mul": v = x * y; return true;
                    case "div": if (y == 0) return false; v = Math.Floor(x / y); return true;
                }
            }
            return false;
        }

        static bool Var(string name, RuleContext cx, out double v)
        {
            v = 0;
            var parts = name.Split('.');
            if (parts.Length != 2) return false;
            if (parts[0] == "family")
            {
                switch (parts[1])
                {
                    case "mood": v = cx.Family.Mood; return true;
                    case "mood_level": v = Family.MoodLevel(cx.Family.Mood); return true;
                    case "assets": v = cx.Family.Assets; return true;
                    case "house": v = cx.Family.HouseGrade; return true;
                }
                return false;
            }
            if (parts[0] == "date")
            {
                switch (parts[1])
                {
                    case "year": v = GameDate.Year(cx.Family.Today); return true;
                    case "month": v = GameDate.Month(cx.Family.Today); return true;
                    case "day": v = GameDate.Day(cx.Family.Today); return true;
                }
                return false;
            }
            Person p;
            if (!cx.Roles.TryGetValue(parts[0], out p)) return false;
            switch (parts[1])
            {
                case "age": v = p.Age(cx.Family.Today); return true;
                case "int": v = p.Stats[0]; return true;
                case "stamina": v = p.Stats[1]; return true;
                case "charm": v = p.Stats[2]; return true;
                case "luck": v = p.Stats[3]; return true;
                case "hearts": v = p.Hearts; return true;
                case "immersion": v = p.Immersion; return true;
                case "job": v = p.Job; return true;
                case "mastery": v = p.JobMastery; return true;
                case "gender": v = p.Gender; return true;
                case "married": v = p.SpouseId >= 0 ? 1 : 0; return true;
            }
            return false;
        }

        // ---------- 효과 ----------
        public static void Apply(List<object> effects, RuleContext cx, List<string> log)
        {
            foreach (var e in effects)
            {
                var d = J.Obj(e); string op = J.Str(d, "op");
                Person t = null;
                cx.Roles.TryGetValue(J.Str(d, "target", "self"), out t);
                int val = J.Int(d, "value");
                switch (op)
                {
                    case "add_stat": if (t != null) t.AddStat(Stat.FromKey(J.Str(d, "stat")), val); break;
                    case "add_hearts": if (t != null) t.AddHearts(val); break;
                    case "add_immersion": if (t != null) t.Immersion = Math.Max(0, Math.Min(255, t.Immersion + val)); break;
                    case "add_job_mastery": if (t != null) t.JobMastery = Math.Max(0, t.JobMastery + val); break;
                    case "set_job": if (t != null) { t.Job = val; t.JobMastery = 0; } break;
                    case "add_skill": if (t != null && !t.Skills.Contains(val)) t.Skills.Add(val); break;
                    case "remove_skill": if (t != null) t.Skills.Remove(val); break;
                    case "family_mood": cx.Family.Mood = Math.Max(0, Math.Min(255, cx.Family.Mood + val)); break;
                    case "family_assets": cx.Family.Assets += val; break;
                    case "set_flag":
                    case "clear_flag":
                        bool set = op == "set_flag"; string n = J.Str(d, "name");
                        if (J.Str(d, "scope") == "family") { if (set) cx.Family.Flags.Add(n); else cx.Family.Flags.Remove(n); }
                        else if (t != null) { if (set) t.Flags.Add(n); else t.Flags.Remove(n); }
                        break;
                    case "set_planned_state":
                        if (t != null) { t.PlannedStateId = J.Str(d, "id"); t.PlannedDue = cx.Family.Today + Math.Max(1, J.Int(d, "delayDays", 30)); }
                        break;
                    case "log": if (log != null) log.Add(J.Str(d, "text")); break;
                }
            }
        }
    }
}

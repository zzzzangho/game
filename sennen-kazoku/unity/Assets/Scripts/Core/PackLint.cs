using System;
using System.Collections.Generic;
using System.Text;

namespace SennenKazoku.Core
{
    /// <summary>제작자용 검사·미리보기 (Unity 에디터 창과 dotnet CLI 가 공유).</summary>
    public static class PackLint
    {
        /// <summary>단독 검증 + 기준 팩들과의 교차 검증. 문제가 없으면 빈 목록.</summary>
        public static List<string> Check(string json, List<Pack> baseline)
        {
            var errs = new List<string>();
            var p = Pack.Load(json, errs);
            if (p == null) return errs;
            var all = new List<Pack>();
            foreach (var b in baseline) if (b.PackId != p.PackId) all.Add(b);
            all.Add(p);
            ContentCatalog.Build(all, errs);
            // 경고성 점검: 도달 불가능해 보이는 설정
            foreach (var e in p.Events)
            {
                if (e.Kind == "standalone" && e.Condition == null) errs.Add("경고 " + e.Id + ": trigger.condition 이 없어 모든 인물에게 항상 후보가 됩니다");
                if (e.Kind == "outcome")
                {
                    bool used = false;
                    foreach (var pk in all) foreach (var s in pk.States) if (s.Outcomes.Contains(e.Id)) used = true;
                    if (!used) errs.Add("경고 " + e.Id + ": outcome 이벤트인데 어떤 예정 상태에서도 참조되지 않습니다");
                }
            }
            return errs;
        }

        public static bool HasErrors(List<string> report) { return report.Exists(m => !m.StartsWith("경고")); }

        /// <summary>이벤트를 가상의 가족에게 강제로 일으켜 모든 선택지 경로를 텍스트로 출력한다.</summary>
        public static string Preview(string json, List<Pack> baseline, string eventId, int personIndex = 0)
        {
            var errs = new List<string>();
            var p = Pack.Load(json, errs);
            if (p == null) return "팩 오류:\n" + string.Join("\n", errs);
            var all = new List<Pack>();
            foreach (var b in baseline) if (b.PackId != p.PackId) all.Add(b);
            all.Add(p);
            var cat = ContentCatalog.Build(all, errs);
            if (cat == null) return "교차 검증 오류:\n" + string.Join("\n", errs);
            EventDef def;
            if (!cat.Events.TryGetValue(eventId, out def)) return "이벤트 없음: " + eventId;
            var sb = new StringBuilder();
            sb.AppendLine("== " + def.Id + " v" + def.Version + " [" + def.Origin + "/" + def.Certainty + "] " + def.Title);
            var branches = new List<string> { "" };
            // 첫 실행으로 선택지 목록을 얻은 뒤 분기별로 다시 실행
            for (int pass = 0; pass < branches.Count; pass++)
            {
                var f = NewGame.Create(1);
                var s = new GameSession(f, cat);
                var person = f.Members[Math.Min(personIndex, f.Members.Count - 1)];
                if (!s.ForceStart(eventId, person.Id)) { sb.AppendLine("  (미리보기 가족에서 역할을 채울 수 없음: roles 확인)"); break; }
                string want = branches[pass];
                if (pass > 0) sb.AppendLine("-- 분기: " + want);
                int guard = 0;
                while (s.Paused && guard++ < 200)
                {
                    var v = s.View();
                    if (v.NeedsChoice)
                    {
                        sb.AppendLine("  [" + v.Speaker + "] " + v.Text);
                        if (pass == 0 && branches.Count == 1) foreach (var c in v.Choices) branches.Add(c.Id);
                        foreach (var c in v.Choices) sb.AppendLine("    ○ " + c.Id + ": " + c.Text);
                        string pick = want.Length > 0 ? want : (v.Choices.Count > 0 ? v.Choices[0].Id : "");
                        if (pass == 0) sb.AppendLine("    → (첫 분기 '" + pick + "' 진행)");
                        s.Choose(pick);
                    }
                    else { sb.AppendLine("  [" + v.Speaker + "] " + v.Text); s.Advance(); }
                }
                if (pass == 0 && branches.Count > 1) branches.RemoveAt(1);   // 첫 분기는 이미 출력됨
                sb.AppendLine("  결과: 무드 " + f.Mood + ", " + person.Name + " 하트 " + person.Hearts + ", 지력 " + person.Stats[0] + ", 플래그 " + string.Join(",", f.Flags));
            }
            return sb.ToString();
        }
    }
}

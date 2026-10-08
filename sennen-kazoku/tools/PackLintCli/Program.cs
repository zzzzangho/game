using System;
using System.Collections.Generic;
using System.IO;
using SennenKazoku.Core;

// 사용: dotnet run -- <기준 팩 디렉터리> <검사할 팩.json> [--preview 이벤트id]
static class Program
{
    // 로컬 팩 통합 시뮬레이션: 모든 규칙이 로딩되고 예정 상태→결과가 실제로 굴러가는지 통계로 확인.
    static void Simulate(string json, List<Pack> baseline, int years)
    {
        var errs = new List<string>(); var p = Pack.Load(json, errs);
        var all = new List<Pack>(); foreach (var b in baseline) if (b.PackId != p.PackId) all.Add(b); all.Add(p);
        var cat = ContentCatalog.Build(all, errs);
        var hist = new Dictionary<string, int>(); int events = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var f = NewGame.Create(seed); var s = new GameSession(f, cat);
            for (int d = 0; d < years * 360; d++)
            {
                if (s.StepDay()) while (s.Paused) { var v = s.View(); if (v.NeedsChoice) s.Choose(v.Choices[0].Id); else s.Advance(); }
            }
            foreach (var h in f.History) { events++; int n; hist.TryGetValue(h.EventId, out n); hist[h.EventId] = n + 1; }
            foreach (var m in f.Members) if (m.Hearts < 0 || m.Hearts > Person.HeartMax || m.Stats[0] < 0 || m.Stats[0] > 5000) throw new Exception("범위 위반");
        }
        Console.WriteLine("시뮬레이션: 5가족 x " + years + "년, 이벤트 " + events + "건, 서로 다른 이벤트 " + hist.Count + "종, " + sw.ElapsedMilliseconds + "ms");
        var top = new List<KeyValuePair<string, int>>(hist); top.Sort((x, y) => y.Value - x.Value);
        for (int i = 0; i < Math.Min(5, top.Count); i++) Console.WriteLine("  " + top[i].Key + " x" + top[i].Value + "  " + cat.Events[top[i].Key].Title);
    }

    static int Main(string[] a)
    {
        if (a.Length < 2) { Console.WriteLine("사용: PackLintCli <기준 팩 디렉터리> <팩.json> [--preview 이벤트id ...]"); return 2; }
        var baseline = new List<Pack>();
        foreach (var f in Directory.GetFiles(a[0], "*.json"))
        {
            var e = new List<string>(); var p = Pack.Load(File.ReadAllText(f), e);
            if (p != null) baseline.Add(p); else Console.WriteLine("(기준 팩 건너뜀 " + Path.GetFileName(f) + ": " + e[0] + ")");
        }
        string json = File.ReadAllText(a[1]);
        var rep = PackLint.Check(json, baseline);
        foreach (var m in rep) Console.WriteLine(m);
        bool bad = PackLint.HasErrors(rep);
        Console.WriteLine(bad ? "검사 실패" : "검사 통과" + (rep.Count > 0 ? " (경고 " + rep.Count + ")" : ""));
        for (int i = 2; i < a.Length - 1; i++)
            if (a[i] == "--preview") Console.WriteLine(PackLint.Preview(json, baseline, a[i + 1]));
        for (int i = 2; i < a.Length - 1; i++)
            if (a[i] == "--simulate") Simulate(json, baseline, int.Parse(a[i + 1]));
        return bad ? 1 : 0;
    }
}

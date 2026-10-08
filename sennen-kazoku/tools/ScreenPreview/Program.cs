using System;
using System.Collections.Generic;
using System.IO;
using SennenKazoku.Core;

// 실제 코어(세션·레이아웃)가 만든 값을 JSON 으로 덤프한다. tools/render_preview.py 가 이 값을 그려 레이아웃 미리보기를 만든다.
// (Unity 렌더링이 아니라 같은 수식을 쓴 근사 그림이다.)
static class P
{
    static List<Pack> Load(string dir)
    {
        var l = new List<Pack>();
        foreach (var f in Directory.GetFiles(dir, "*.json")) { var e = new List<string>(); var p = Pack.Load(File.ReadAllText(f), e); if (p != null) l.Add(p); }
        return l;
    }
    static object R(RectPx r) { return new List<object> { (double)r.X, (double)r.Y, (double)r.W, (double)r.H }; }

    static int Main(string[] a)
    {
        var cat = ContentCatalog.Build(Load(a[0]), new List<string>());
        var devices = new[] { new[] { 1080, 1920, 0, 0 }, new[] { 1080, 2160, 0, 0 }, new[] { 1080, 2340, 66, 0 }, new[] { 1080, 2400, 100, 48 }, new[] { 720, 1280, 0, 0 }, new[] { 360, 640, 0, 0 }, new[] { 360, 568, 20, 0 }, new[] { 1668, 2388, 24, 20 } };
        var outList = new List<object>();
        foreach (var d in devices)
        {
            float w = d[0], h = d[1];
            var lay = LayoutCalculator.Compute(w, h, 0, d[2], w, h - d[2] - d[3], 160f * (w / 360f));
            var sfPath = a.Length > 1 ? a[1] : "";
            var f = File.Exists(sfPath) ? NewGame.FromOriginal(J.Obj(MiniJson.Parse(File.ReadAllText(sfPath))), 7) : NewGame.Create(7);
            var s = new GameSession(f, cat);
            for (int i = 0; i < 3; i++) s.StepDay();
            string idleText = "시간이 흘러가고 있습니다…"; var members = new List<object>();
            foreach (var p in f.Members) members.Add(new Dictionary<string, object> { { "name", p.Name }, { "age", p.Age(f.Today) }, { "rank", Stat.Rank(p.Stats[0]) }, { "g", p.Gender },
                { "ranks", new List<object> { Stat.Rank(p.Stats[0]), Stat.Rank(p.Stats[1]), Stat.Rank(p.Stats[2]), Stat.Rank(p.Stats[3]) } }, { "hearts", p.Hearts }, { "imm", p.Immersion },
                { "character", p.Character }, { "planned", p.PlannedTitle }, { "head", p.Id == f.HeadId }, { "job", p.Job } });
            var idle = new Dictionary<string, object> { { "date", GameDate.Format(f.Today) }, { "mood", Family.MoodLevel(f.Mood) }, { "assets", f.Assets }, { "family", f.Name }, { "members", members }, { "text", idleText } };
            s.ForceStart("nova.picnic.001", 1); s.Advance(); s.Advance(); var v = s.View();
            var ch = new List<object>(); foreach (var c in v.Choices) ch.Add(c.Text);
            var ev = new Dictionary<string, object> { { "title", v.Title }, { "speaker", v.Speaker }, { "text", v.Text }, { "choices", ch }, { "badge", "신규 이벤트 · 규칙 임시 · 임시 문구" } };
            outList.Add(new Dictionary<string, object> {
                { "w", (int)w }, { "h", (int)h }, { "safeTop", d[2] }, { "safeBottom", d[3] }, { "dp", (double)lay.Dp },
                { "rects", new Dictionary<string, object> { { "top", R(lay.TopBar) }, { "strip", R(lay.FamilyStrip) }, { "scene", R(lay.Scene) }, { "panel", R(lay.EventPanel) }, { "controls", R(lay.Controls) } } },
                { "idle", idle }, { "event", ev }, { "houseScale", (double)lay.HouseScale }, { "years", f.YearsAsFamily },
                { "arrows", new List<object> { Interventions.Count(f, "arrow.encourage"), Interventions.Count(f, "arrow.calm") } } });
        }
        Console.WriteLine(MiniJson.Serialize(outList));
        return 0;
    }
}

using System;
using System.Collections.Generic;

namespace SennenKazoku.Core
{
    public sealed class ToolDef
    {
        public string Id, Name, Kind, Desc, Certainty; public bool Implemented;
    }

    /// <summary>
    /// 플레이어(신님·큐피트)의 개입: 화살과 아이템.
    /// 화살 (ROM 0x080244A0 해독 + 에뮬레이터 측정): 힘내라 = 화살표시 |= 3, 진정해 비트 해제 / 진정해 = |= 5, 힘내라 비트 해제.
    /// 효과는 매일 열중 게이지에 적용(힘내라 +32, 진정해 −64)되고, 관심사가 끝날 때(MAX/MIN 사건) 표시가 0 으로 풀린다.
    /// 효과 중에는 힘내라·진정해를 다시 쓸 수 없음 [원작 화면 문구]. 시작 보유 각 5개(원작 튜토리얼 화면).
    /// 고리 = 해당 능력 +800(상한 5000), 행복 상자 = 무드 한 단계 [item-effects.json]. 하트 열매의 하트 단위는 미해명.
    /// </summary>
    public static class Interventions
    {
        public static readonly List<ToolDef> Tools = new List<ToolDef> {
            new ToolDef { Id = "arrow.encourage", Name = "힘내라의 화살", Kind = "arrow", Desc = "현재 관심사에 몰두하는 마음을 응원하는 화살.", Certainty = "confirmed", Implemented = true },
            new ToolDef { Id = "arrow.calm", Name = "진정해의 화살", Kind = "arrow", Desc = "현재 관심사에 대한 열기를 식히는 화살.", Certainty = "confirmed", Implemented = true },
            new ToolDef { Id = "arrow.love", Name = "애정 붐의 화살", Kind = "arrow", Desc = "애정 붐 미터 +1000 (미터의 의미 미확정 → 미구현)", Certainty = "estimated", Implemented = false },
            new ToolDef { Id = "arrow.encounter", Name = "만남의 예감의 화살", Kind = "arrow", Desc = "만남·연애 관계 상태 갱신 (연애 시스템 미구현)", Certainty = "estimated", Implemented = false },
            new ToolDef { Id = "item.ring.int", Name = "지력의 고리", Kind = "item", Desc = "지력이 한 등급 오릅니다.", Certainty = "confirmed", Implemented = true },
            new ToolDef { Id = "item.ring.stamina", Name = "체력의 고리", Kind = "item", Desc = "체력이 한 등급 오릅니다.", Certainty = "confirmed", Implemented = true },
            new ToolDef { Id = "item.ring.charm", Name = "매력의 고리", Kind = "item", Desc = "매력이 한 등급 오릅니다.", Certainty = "confirmed", Implemented = true },
            new ToolDef { Id = "item.ring.luck", Name = "운의 고리", Kind = "item", Desc = "운이 한 등급 오릅니다.", Certainty = "confirmed", Implemented = true },
            new ToolDef { Id = "item.happiness_box", Name = "행복 상자", Kind = "item", Desc = "가족 무드가 한 단계 좋아집니다.", Certainty = "confirmed", Implemented = true },
            new ToolDef { Id = "item.heart_fruit", Name = "하트 열매", Kind = "item", Desc = "하트를 1개 회복합니다.", Certainty = "estimated", Implemented = true },
            new ToolDef { Id = "item.poison_heart_fruit", Name = "독 하트 열매", Kind = "item", Desc = "하트를 모두 없앱니다.", Certainty = "confirmed", Implemented = true },
        };

        public static ToolDef Find(string id) { return Tools.Find(t => t.Id == id); }

        public static void GiveStarting(Family f)
        {
            f.Items["arrow.encourage"] = 5; f.Items["arrow.calm"] = 5;   // 원작 튜토리얼 화면: 각 ×5
        }

        public static int Count(Family f, string id) { int n; return f.Items.TryGetValue(id, out n) ? n : 0; }

        /// <summary>사용 시도. 성공하면 null, 실패하면 이유 문자열.</summary>
        public static string Use(Family f, Person p, string id)
        {
            var t = Find(id);
            if (t == null) return "알 수 없는 도구";
            if (!t.Implemented) return t.Name + "은(는) 아직 구현되지 않았습니다";
            if (Count(f, id) <= 0) return t.Name + "이(가) 없습니다";
            if (p == null || !p.Alive) return "대상이 없습니다";
            if (t.Kind == "arrow" && (id == "arrow.encourage" || id == "arrow.calm") && (p.ArrowFlags & 1) != 0)
                return "화살의 효과가 계속되고 있습니다. 힘내라의 화살·진정해의 화살은 쏠 수 없습니다";
            switch (id)
            {
                case "arrow.encourage": p.ArrowFlags = (p.ArrowFlags | 3) & ~4; break;
                case "arrow.calm": p.ArrowFlags = (p.ArrowFlags | 5) & ~2; break;
                case "item.ring.int": p.Stats[0] = Math.Min(Stat.Max, p.Stats[0] + 800); break;
                case "item.ring.stamina": p.Stats[1] = Math.Min(Stat.Max, p.Stats[1] + 800); break;
                case "item.ring.charm": p.Stats[2] = Math.Min(Stat.Max, p.Stats[2] + 800); break;
                case "item.ring.luck": p.Stats[3] = Math.Min(Stat.Max, p.Stats[3] + 800); break;
                case "item.happiness_box":
                    int[] reps = { 24, 72, 128, 184, 232 };            // item-effects.json mood_levels 대표값
                    f.Mood = reps[Math.Min(4, Family.MoodLevel(f.Mood))]; break;
                case "item.heart_fruit": p.AddHearts(Person.HeartUnit); break;
                case "item.poison_heart_fruit": p.Hearts = 0; break;
            }
            if (t.Kind == "arrow") p.ArrowId = id;
            f.Items[id] = Count(f, id) - 1;
            return null;
        }

        /// <summary>하루 처리 (화살 효과는 관심사 종료 때 GameSession 이 푼다 — 기간 만료 없음).</summary>
        public static void Tick(Family f) { }
    }
}

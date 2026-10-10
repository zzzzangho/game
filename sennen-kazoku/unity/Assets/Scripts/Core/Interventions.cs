using System;
using System.Collections.Generic;

namespace SennenKazoku.Core
{
    public sealed class ToolDef
    {
        public string Id, Name, Kind, Desc, Certainty; public bool Implemented;
        /// <summary>원작 보유 칸 번호: 화살 0x0202C640+칸, 아이템 0x0202C650+칸 (실기 메뉴에서 칸마다 개수를 넣어 확인). 원작 함수의 종류 번호와 같다.</summary>
        public int OrigSlot = -1;
    }

    /// <summary>
    /// 플레이어(신님·큐피트)의 개입: 화살과 아이템.
    /// 화살 (ROM 0x080244A0 해독 + 에뮬레이터 측정): 힘내라 = 화살표시 |= 3, 진정해 비트 해제 / 진정해 = |= 5, 힘내라 비트 해제.
    /// 효과는 매일 열중 게이지에 적용(힘내라 +32, 진정해 −64)되고, 관심사가 끝날 때(MAX/MIN 사건) 표시가 0 으로 풀린다.
    /// 효과 중에는 힘내라·진정해를 다시 쓸 수 없음 [원작 화면 문구]. 시작 보유 각 5개(원작 튜토리얼 화면).
    /// 고리 = 해당 능력 +1000(상한 5000), 행복 상자 = 무드 +64(상한 255) [원작 아이템 함수 0x08024B80 해독, 고리는 실기에서 +1000 확인 —
    /// 참고 자료 item-effects.json 의 +800 은 원작과 다르다]. 하트 열매(+0x5B +95)는 이 옛 경로의 하트 칸 단위로만 흉내 낸다.
    /// 원작 세션(OrigSession)은 이 표의 효과를 쓰지 않고 원작 함수를 그대로 부른다.
    /// </summary>
    public static class Interventions
    {
        public static readonly List<ToolDef> Tools = new List<ToolDef> {
            new ToolDef { Id = "arrow.encourage", Name = "힘내라의 화살", Kind = "arrow", Desc = "현재 관심사에 몰두하는 마음을 응원하는 화살.", Certainty = "confirmed", Implemented = true, OrigSlot = 0 },
            new ToolDef { Id = "arrow.calm", Name = "진정해의 화살", Kind = "arrow", Desc = "현재 관심사에 대한 열기를 식히는 화살.", Certainty = "confirmed", Implemented = true, OrigSlot = 1 },
            new ToolDef { Id = "arrow.easy", Name = "태평함 붐의 화살", Kind = "arrow", Desc = "몇 년 동안 편한 일에 마음이 가게 하는 화살.", Certainty = "rom", OrigSlot = 2 },
            new ToolDef { Id = "arrow.effort", Name = "노력 붐의 화살", Kind = "arrow", Desc = "몇 년 동안 꿈·공부·일에 마음이 가게 하는 화살.", Certainty = "rom", OrigSlot = 3 },
            new ToolDef { Id = "arrow.love", Name = "애정 붐의 화살", Kind = "arrow", Desc = "몇 년 동안 여러 사람에게 마음이 가게 하는 화살.", Certainty = "rom", OrigSlot = 4 },
            new ToolDef { Id = "arrow.single", Name = "독신 붐의 화살", Kind = "arrow", Desc = "한동안 결혼할 마음이 없어지게 하는 화살.", Certainty = "rom", OrigSlot = 5 },
            new ToolDef { Id = "arrow.myway", Name = "마이웨이의 화살", Kind = "arrow", Desc = "몇 년 동안 다른 화살이 듣지 않는 대신 고마움을 많이 받는 화살.", Certainty = "rom", OrigSlot = 6 },
            new ToolDef { Id = "arrow.ambition", Name = "대망의 화살", Kind = "arrow", Desc = "마음속에 큰 꿈이 생기게 하는 화살.", Certainty = "rom", OrigSlot = 7 },
            new ToolDef { Id = "arrow.legacy", Name = "선대의 꿈의 화살", Kind = "arrow", Desc = "앞 세대가 남긴 꿈을 이어받게 하는 화살.", Certainty = "rom", OrigSlot = 8 },
            new ToolDef { Id = "arrow.encounter", Name = "만남의 예감의 화살", Kind = "arrow", Desc = "좋은 사람을 만날 만한 일에 마음이 가게 하는 화살.", Certainty = "rom", OrigSlot = 9 },
            new ToolDef { Id = "arrow.link", Name = "통신 결혼의 화살", Kind = "arrow", Desc = "다른 가족과 통신으로 결혼시키는 화살 (통신 기능은 옮기지 않음).", Certainty = "rom", OrigSlot = 10 },
            new ToolDef { Id = "item.heart_fruit", Name = "하트 열매", Kind = "item", Desc = "하트를 1개 회복합니다.", Certainty = "estimated", Implemented = true, OrigSlot = 0 },
            new ToolDef { Id = "item.poison_heart_fruit", Name = "독 하트 열매", Kind = "item", Desc = "하트를 모두 없앱니다.", Certainty = "confirmed", Implemented = true, OrigSlot = 1 },
            new ToolDef { Id = "item.torch", Name = "액막이 횃불", Kind = "item", Desc = "악마를 쫓아내는 횃불.", Certainty = "rom", OrigSlot = 2 },
            new ToolDef { Id = "item.happiness_box", Name = "행복 상자", Kind = "item", Desc = "가족 무드가 한 단계 좋아집니다.", Certainty = "confirmed", Implemented = true, OrigSlot = 3 },
            new ToolDef { Id = "item.ring.int", Name = "지력의 고리", Kind = "item", Desc = "지력이 한 등급 오릅니다.", Certainty = "confirmed", Implemented = true, OrigSlot = 4 },
            new ToolDef { Id = "item.ring.stamina", Name = "체력의 고리", Kind = "item", Desc = "체력이 한 등급 오릅니다.", Certainty = "confirmed", Implemented = true, OrigSlot = 5 },
            new ToolDef { Id = "item.ring.charm", Name = "매력의 고리", Kind = "item", Desc = "매력이 한 등급 오릅니다.", Certainty = "confirmed", Implemented = true, OrigSlot = 6 },
            new ToolDef { Id = "item.ring.luck", Name = "운의 고리", Kind = "item", Desc = "운이 한 등급 오릅니다.", Certainty = "confirmed", Implemented = true, OrigSlot = 7 },
            new ToolDef { Id = "item.ring.love", Name = "사랑의 고리", Kind = "item", Desc = "연인에 대한 사랑을 깊게 합니다 (연인이 있을 때만).", Certainty = "rom", OrigSlot = 8 },
            new ToolDef { Id = "item.crown", Name = "후계자의 왕관", Kind = "item", Desc = "세대주의 자녀에게 쓰면 다음 세대주 후보가 됩니다.", Certainty = "rom", OrigSlot = 9 },
            new ToolDef { Id = "item.bookmark", Name = "시간의 책갈피", Kind = "item", Desc = "가문이 끊겼을 때 쓴 날로 돌아가는 책갈피 (아직 옮기지 않음).", Certainty = "rom", OrigSlot = 10 },
            new ToolDef { Id = "item.heart_crystal", Name = "선대 마음의 결정", Kind = "item", Desc = "앞 세대가 익힌 스킬을 이어받게 합니다.", Certainty = "rom", OrigSlot = 11 },
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
                case "item.ring.int": p.Stats[0] = Math.Min(Stat.Max, p.Stats[0] + 1000); break;
                case "item.ring.stamina": p.Stats[1] = Math.Min(Stat.Max, p.Stats[1] + 1000); break;
                case "item.ring.charm": p.Stats[2] = Math.Min(Stat.Max, p.Stats[2] + 1000); break;
                case "item.ring.luck": p.Stats[3] = Math.Min(Stat.Max, p.Stats[3] + 1000); break;
                case "item.happiness_box": f.Mood = Math.Min(255, f.Mood + 64); break;   // 원작 0x08024EC6: 무드 +0x40 (상한 255)
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

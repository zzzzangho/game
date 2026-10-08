using System.Collections.Generic;

namespace SennenKazoku.Core
{
    /// <summary>
    /// 가족 구조에서 계산하는 관계 판정. 원작의 "관계/상태 코드" 일부를 의미 추정으로 옮긴 것이다
    /// (근거: docs/06_관계상태코드_추정.md). 추정이므로 원작과 다를 수 있다.
    ///   spouse       ≈ 코드 15 / 인물 슬롯 15 유효   (배우자 있음)
    ///   lover        ≈ 코드 28                       (연인 있음, 미혼) — 연애 시스템이 없어 플래그 "lover" 로만 표현
    ///   child_spouse ≈ 코드 16                       (자녀의 배우자 = 며느리/사위 있음)
    ///   grandchild   ≈ 코드 18                       (손주 있음)
    /// </summary>
    public static class Relations
    {
        public static readonly HashSet<string> Names = new HashSet<string> { "spouse", "lover", "child_spouse", "grandchild" };

        public static bool Has(Family f, Person p, string name)
        {
            switch (name)
            {
                case "spouse": return p.SpouseId >= 0 && f.Get(p.SpouseId) != null;
                case "lover": return p.SpouseId < 0 && p.Flags.Contains("lover");
                case "child_spouse":
                    foreach (var c in Children(f, p)) if (c.SpouseId >= 0) return true;
                    return false;
                case "grandchild":
                    foreach (var c in Children(f, p)) foreach (var _ in Children(f, c)) return true;
                    return false;
            }
            return false;
        }

        static IEnumerable<Person> Children(Family f, Person p)
        {
            foreach (var m in f.Members) if (m.FatherId == p.Id || m.MotherId == p.Id) yield return m;
        }
    }
}

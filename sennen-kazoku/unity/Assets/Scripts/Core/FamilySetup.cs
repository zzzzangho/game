using System;
using System.Collections.Generic;

namespace SennenKazoku.Core
{
    /// <summary>원작 '가족 구성' 칸. 자녀 칸은 4개(아들/딸 선택).</summary>
    public enum FamilyRole { Grandfather, Grandmother, Father, Mother, Child }

    /// <summary>원작 '내가 아는 가족' 구성원 입력 (원작 화면 순서: 이름 → 생일 → 혈액형 → 성격 → 능력 순위 → 직업 → 캐릭터).</summary>
    public sealed class MemberSetup
    {
        public FamilyRole Role;
        public string Name = "";
        public int Gender;                       // 할아버지·아버지 남, 할머니·어머니 여, 자녀는 선택
        public int BirthDay;
        public string Blood = "?";               // A / B / O / AB / ?  (원작 선택지)
        public int Personality = 1;              // 0 내향적 · 1 보통 · 2 외향적 (원작 선택지)
        public int[] AbilityRank = { 1, 2, 3, 4 };   // 지력·체력·매력·운 각각의 순위(1~4, 서로 다름)
        public int Job;                          // 원작 직업 코드 (이름표는 로컬 자료)
        public CharacterLook Look;

        public static readonly string[] Bloods = { "A", "B", "O", "AB", "?" };
        public static readonly string[] Personalities = { "내향적", "보통", "외향적" };
    }

    public sealed class FamilySetup
    {
        public int StartDay = GameDate.Make(2005, 1, 1);     // 원작 기본값 2005-01-01 (확인됨)
        public string Surname = "";
        public readonly List<MemberSetup> Members = new List<MemberSetup>();

        /// <summary>원작 기본 생년(시작 연도 기준, 원작 입력 화면 관찰): 조부모 −37, 부모 −19, 자녀 −1. 월일은 1월 1일.</summary>
        public static int DefaultBirth(FamilyRole r, int startDay)
        {
            int y = GameDate.Year(startDay);
            int off = r == FamilyRole.Grandfather || r == FamilyRole.Grandmother ? 37 : r == FamilyRole.Child ? 1 : 19;
            return GameDate.Make(y - off, 1, 1);
        }

        public MemberSetup Add(FamilyRole r, int childGender = 0)
        {
            var m = new MemberSetup { Role = r, BirthDay = DefaultBirth(r, StartDay),
                Gender = r == FamilyRole.Grandmother || r == FamilyRole.Mother ? 1 : r == FamilyRole.Child ? childGender : 0 };
            Members.Add(m);
            return m;
        }

        /// <summary>원작은 캐릭터 선택을 7세 이상에게만 묻는다(6세 이하는 건너뜀 — 확인됨).</summary>
        public static bool AsksCharacter(MemberSetup m, int startDay) { return GameDate.AgeYears(m.BirthDay, startDay) >= 7; }

        /// <summary>원작 캐릭터 목록 구분(확인됨): 조부·조모·부·모, 자녀는 7~12세 / 13세~ 목록이 다르다.</summary>
        public static string GalleryRole(MemberSetup m, int startDay)
        {
            int age = GameDate.AgeYears(m.BirthDay, startDay);
            switch (m.Role)
            {
                case FamilyRole.Grandfather: return "grandfather";
                case FamilyRole.Grandmother: return "grandmother";
                case FamilyRole.Father: return "father";
                case FamilyRole.Mother: return "mother";
                default: return m.Gender == 0 ? (age >= 13 ? "son18" : "son7") : (age >= 13 ? "daughter13" : "daughter7");
            }
        }

        public List<string> Validate()
        {
            var e = new List<string>();
            if (Members.Count == 0) e.Add("가족 구성원이 없습니다");
            int children = 0; var seen = new HashSet<FamilyRole>();
            foreach (var m in Members)
            {
                if (m.Role == FamilyRole.Child) children++;
                else if (!seen.Add(m.Role)) e.Add(m.Role + " 칸이 둘 이상입니다");
                if (string.IsNullOrEmpty(m.Name)) e.Add("이름이 비어 있는 구성원이 있습니다");
                if (m.BirthDay > StartDay) e.Add(m.Name + ": 생일이 시작일보다 늦습니다");
                var r = new HashSet<int>(m.AbilityRank);
                if (m.AbilityRank.Length != 4 || r.Count != 4 || !r.SetEquals(new[] { 1, 2, 3, 4 })) e.Add(m.Name + ": 능력 순위는 1~4번을 한 번씩");
            }
            if (children > 4) e.Add("자녀 칸은 4개까지");
            return e;
        }

        /// <summary>
        /// 능력 순위 → 시작 능력치. 추정: 원작에서 만든 가족 1개(37세 2명, 18·19세 2명, 모두 지력1·체력2·매력3·운4)의 실제 값으로 만든 표.
        /// 37세: 2814/2344/1874/1600 · 18~19세: 지력 1914, 체력 1964, 매력 1560, 운 1600 (순위별 값과 능력별 값을 분리하지 못했다).
        /// </summary>
        public static int StartStat(int stat, int rank, int age, Rng rng)
        {
            int[] adult = { 2814, 2344, 1874, 1600 };
            int v = adult[Math.Max(0, Math.Min(3, rank - 1))];
            if (age < 20 && rank < 4) v = 1600 + (v - 1600) * 3 / 10 + (stat == Stat.Stamina ? 100 : 0);
            if (age < 13 && rank < 4) v = v * 7 / 10;
            return Math.Max(0, Math.Min(Stat.Max, v + (rank < 4 ? rng.Next(41) - 20 : 0)));
        }

        public Family Build(ulong seed)
        {
            var err = Validate(); if (err.Count > 0) throw new InvalidOperationException(string.Join("; ", err));
            var rng = new Rng(seed);
            var f = new Family { Name = (Surname.Length > 0 ? Surname : "새") + " 가", Today = StartDay, Mood = 128, Assets = 5000, HouseGrade = 2, RngState = seed };
            var ids = new Dictionary<MemberSetup, Person>();
            foreach (var m in Members)
            {
                var p = new Person { Id = f.NextPersonId++, Name = m.Name, Gender = m.Gender, BirthDay = m.BirthDay, Job = m.Job,
                    Hearts = Person.HeartUnit * 3 / 2, Immersion = 82, Look = m.Look == null ? null : m.Look.Clone(), Blood = m.Blood, Personality = m.Personality };
                int age = p.Age(StartDay);
                for (int s = 0; s < 4; s++) p.Stats[s] = StartStat(s, m.AbilityRank[s], age, rng);
                ids[m] = p; f.Members.Add(p);
            }
            Person Find(FamilyRole r) { foreach (var kv in ids) if (kv.Key.Role == r) return kv.Value; return null; }
            var gf = Find(FamilyRole.Grandfather); var gm = Find(FamilyRole.Grandmother);
            var fa = Find(FamilyRole.Father); var mo = Find(FamilyRole.Mother);
            void Marry(Person a, Person b) { if (a != null && b != null) { a.SpouseId = b.Id; b.SpouseId = a.Id; } }
            Marry(gf, gm); Marry(fa, mo);
            // 원작 구성도: 조부모 → 부모 중 한쪽, 부모 → 자녀. 조부모가 누구의 부모인지는 원작에서 미확인 — 아버지 쪽으로 둔다(추정).
            var parentOfMiddle = fa ?? mo;
            if (parentOfMiddle != null) { if (gf != null) parentOfMiddle.FatherId = gf.Id; if (gm != null) parentOfMiddle.MotherId = gm.Id; }
            foreach (var kv in ids)
                if (kv.Key.Role == FamilyRole.Child)
                {
                    if (fa != null) kv.Value.FatherId = fa.Id;
                    if (mo != null) kv.Value.MotherId = mo.Id;
                    if (fa == null && mo == null) { if (gf != null) kv.Value.FatherId = gf.Id; if (gm != null) kv.Value.MotherId = gm.Id; }
                }
            var head = fa ?? mo ?? gf ?? gm ?? f.Members[0];
            f.HeadId = head.Id;
            f.StartDay = f.Today;
            Interventions.GiveStarting(f);
            f.RngState = rng.State;
            return f;
        }
    }
}

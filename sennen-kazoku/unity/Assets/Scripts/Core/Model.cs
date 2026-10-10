using System;
using System.Collections.Generic;

namespace SennenKazoku.Core
{
    /// <summary>
    /// 원작 난수기 (ROM 0x08000614 해독): seed = seed × 0x6D + 0x3FD (32비트), 새 seed 를 그대로 돌려준다.
    /// 원작은 (값 &amp; 15), (값 &amp; 31) 처럼 아래 비트를 쓴다 → Next(2^k) 가 같은 값이 된다. 시작 seed 값은 미해독.
    /// </summary>
    public sealed class Rng
    {
        uint s;
        public Rng(ulong seed) { s = (uint)seed; }
        public ulong State { get { return s; } set { s = (uint)value; } }
        public uint NextU32() { s = unchecked(s * 0x6Du + 0x3FDu); return s; }
        public ulong NextU64() { return ((ulong)NextU32() << 32) | NextU32(); }
        public int Next(int n) { return n <= 1 ? 0 : (int)(NextU32() % (uint)n); }
        public double NextDouble() { return NextU32() / 4294967296.0; }
        /// <summary>num/den 확률 판정.</summary>
        public bool Chance(int num, int den)
        {
            if (den <= 0) return false;
            if (num >= den) return true;
            if (num <= 0) return false;
            return Next(den) < num;
        }
    }

    /// <summary>
    /// 날짜: 그레고리력(원작은 실제 달력·요일을 쓰고 시작 연월일을 플레이어가 정한다 — 원작 화면에서 확인).
    /// dayIndex 0 = 1900-01-01. 하루 = 1틱(진행 속도는 미해명).
    /// </summary>
    public static class GameDate
    {
        static readonly DateTime Epoch = new DateTime(1900, 1, 1);
        public const int DaysPerYear = 365;
        public static readonly string[] WeekdayKo = { "일", "월", "화", "수", "목", "금", "토" };
        static DateTime D(int d) { return Epoch.AddDays(d); }
        public static int Year(int d) { return D(d).Year; }
        public static int Month(int d) { return D(d).Month; }
        public static int Day(int d) { return D(d).Day; }
        public static int Weekday(int d) { return (int)D(d).DayOfWeek; }
        public static int Make(int year, int month, int day)
        {
            day = Math.Max(1, Math.Min(day, DateTime.DaysInMonth(year, month)));
            return (int)(new DateTime(year, month, day) - Epoch).TotalDays;
        }
        /// <summary>만 나이.</summary>
        public static int AgeYears(int birth, int today)
        {
            var b = D(birth); var t = D(today);
            int age = t.Year - b.Year;
            if (t.Month < b.Month || (t.Month == b.Month && t.Day < b.Day)) age--;
            return Math.Max(0, age);
        }
        public static string Format(int d) { return Year(d) + "년 " + Month(d) + "월 " + Day(d) + "일(" + WeekdayKo[Weekday(d)] + ")"; }
    }

    public static class Stat
    {
        public const int Int = 0, Stamina = 1, Charm = 2, Luck = 3;
        public const int Max = 5000;
        public static readonly string[] Names = { "지력", "체력", "매력", "운" };
        /// <summary>능력치 등급 (확인됨: item-effects.json ability_ranks).</summary>
        public static string Rank(int v)
        {
            string[] r = { "F", "D", "C", "B", "A", "S", "SS" };
            int idx = v >= 4800 ? 6 : Math.Min(5, Math.Max(0, v) / 800);
            return r[idx];
        }
        public static int FromKey(string k)
        {
            switch (k) { case "int": return 0; case "stamina": return 1; case "charm": return 2; case "luck": return 3; }
            return -1;
        }
    }

    public sealed class Person
    {
        public int Id;
        public string Name = "";
        public int Gender;                 // 0 남, 1 여
        public int BirthDay;
        public int[] Stats = new int[4];   // 0..5000 (확인됨: 범위·등급)
        public int Hearts;                 // 0..HeartMax. 원작 화면은 하트 3칸(부분 채움) — 칸당 단위는 미해명(임시 96)
        public int Immersion;              // 0..255 (확인됨: 범위)
        /// <summary>신님에게 감사 (원작은 인물마다: 레코드 +0x65 | +0x66&lt;&lt;8, 실기 상세 화면으로 확인). 원작 세션이 매일 투영하며 저장하지 않는다(-1 = 가족 값 사용).</summary>
        public int Gratitude = -1;
        public int Job;                    // 직업 코드 (이름표 미확보)
        public int JobMastery;
        public List<int> Skills = new List<int>();
        public int SpouseId = -1, FatherId = -1, MotherId = -1;
        public string PlannedStateId = "";
        public int PlannedDue = -1;
        public bool Alive = true;
        public HashSet<string> Flags = new HashSet<string>();
        public const int HeartUnit = 96, HeartMax = HeartUnit * 3;
        public string Dream = "";           // 꿈 (원작 상세 화면 항목)
        public string ArrowId = "";         // 맞은 화살 (표시용)
        public int ArrowUntil = -1;         // 사용 안 함(이전 저장 호환). 원작 화살은 기간이 아니라 관심사가 끝날 때까지
        // ---- 원작 인물 레코드에서 해독한 관심사 진행값 ----
        public int Gauge = 136;             // +0x5A 열중 게이지 0~255. 새 관심사는 136 에서 시작 (원작 관찰·코드 확인)
        public int InterestDay = 1;         // +0x48 지금 관심사의 날짜 수
        public int ArrowFlags;              // +0x69 bit0 화살 효과 중 · bit1 힘내라 · bit2 진정해 (ROM 0x080244A0 해독)
        public int PersonalityCode = 1;     // +0x32 하위 4비트: 보통 0 · 내향적 1 · 외향적 2 (원작 가족 만들기로 확인)
        public string Character = "";      // 그래픽 id (LocalArt manifest)
        public string PlannedTitle = "";   // 카탈로그에 없는 원작 예정 상태의 표시용 제목
        public CharacterLook Look;          // 원작 파트 조합 외형 (null = 아직 정하지 않음)
        public string Blood = "?";          // 혈액형 (원작 입력 항목)
        public int Personality = 1;         // 0 내향적 · 1 보통 · 2 외향적 (원작 입력 항목)

        public int Age(int today) { return GameDate.AgeYears(BirthDay, today); }
        public void AddStat(int i, int v) { Stats[i] = Math.Max(0, Math.Min(Stat.Max, Stats[i] + v)); }
        public void AddHearts(int v) { Hearts = Math.Max(0, Math.Min(HeartMax, Hearts + v)); }

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object> {
                {"id", Id}, {"name", Name}, {"gender", Gender}, {"birth", BirthDay},
                {"stats", new List<object>{ Stats[0], Stats[1], Stats[2], Stats[3] }},
                {"hearts", Hearts}, {"immersion", Immersion}, {"job", Job}, {"mastery", JobMastery},
                {"skills", new List<object>(Skills.ConvertAll(x => (object)x))},
                {"spouse", SpouseId}, {"father", FatherId}, {"mother", MotherId},
                {"planned", PlannedStateId}, {"plannedDue", PlannedDue}, {"alive", Alive},
                {"flags", new List<object>(new List<string>(Flags).ConvertAll(x => (object)x))},
                {"dream", Dream}, {"arrow", ArrowId}, {"arrowUntil", ArrowUntil}, {"character", Character}, {"plannedTitle", PlannedTitle},
                {"look", Look == null ? null : Look.ToJson()}, {"blood", Blood}, {"personality", Personality},
                {"gauge", Gauge}, {"interestDay", InterestDay}, {"arrowFlags", ArrowFlags}, {"pcode", PersonalityCode}
            };
        }
        public static Person FromJson(Dictionary<string, object> d)
        {
            var p = new Person {
                Id = J.Int(d, "id"), Name = J.Str(d, "name"), Gender = J.Int(d, "gender"), BirthDay = J.Int(d, "birth"),
                Hearts = J.Int(d, "hearts"), Immersion = J.Int(d, "immersion"), Job = J.Int(d, "job"), JobMastery = J.Int(d, "mastery"),
                SpouseId = J.Int(d, "spouse", -1), FatherId = J.Int(d, "father", -1), MotherId = J.Int(d, "mother", -1),
                PlannedStateId = J.Str(d, "planned"), PlannedDue = J.Int(d, "plannedDue", -1), Alive = J.Bool(d, "alive", true)
            };
            var st = J.List(d, "stats");
            for (int i = 0; i < 4 && i < st.Count; i++) p.Stats[i] = Convert.ToInt32(st[i]);
            foreach (var s in J.List(d, "skills")) p.Skills.Add(Convert.ToInt32(s));
            foreach (var f in J.List(d, "flags")) p.Flags.Add((string)f);
            p.Dream = J.Str(d, "dream"); p.ArrowId = J.Str(d, "arrow"); p.ArrowUntil = J.Int(d, "arrowUntil", -1);
            p.Character = J.Str(d, "character"); p.PlannedTitle = J.Str(d, "plannedTitle");
            p.Look = CharacterLook.FromJson(J.Child(d, "look"));
            p.Blood = J.Str(d, "blood", "?"); p.Personality = J.Int(d, "personality", 1);
            p.Gauge = J.Int(d, "gauge", 136); p.InterestDay = J.Int(d, "interestDay", 1); p.ArrowFlags = J.Int(d, "arrowFlags"); p.PersonalityCode = J.Int(d, "pcode", 1);
            return p;
        }
    }

    public sealed class EventRecord
    {
        public int Day; public string EventId = ""; public int EventVersion; public string Title = "";
        public int PersonId; public string Choice = "";
        public string Changes = "";           // 이 사건으로 바뀐 것 (예: "무드+12;타로.하트-30;타로.체력+50")
    }

    /// <summary>진행 중 이벤트. 스냅샷을 함께 저장해 팩 업데이트로 깨지지 않는다.</summary>
    public sealed class ActiveEvent
    {
        public string EventId = ""; public int EventVersion;
        public string SnapshotJson = "";      // 시작 시점의 이벤트 정의 전체
        public Dictionary<string, int> Cast = new Dictionary<string, int>();
        public int PageIndex;                 // 현재 표시 중인 페이지
        public string Phase = "pages";        // pages | choices | result
        public string ChosenId = "";
        public Dictionary<string, int> Before = new Dictionary<string, int>();   // 시작 시점 수치 (사건 결과 표시용)

        public Dictionary<string, object> ToJson()
        {
            var cast = new Dictionary<string, object>();
            foreach (var kv in Cast) cast[kv.Key] = kv.Value;
            var before = new Dictionary<string, object>();
            foreach (var kv in Before) before[kv.Key] = kv.Value;
            return new Dictionary<string, object> {
                {"id", EventId}, {"version", EventVersion}, {"snapshot", SnapshotJson}, {"cast", cast},
                {"page", PageIndex}, {"phase", Phase}, {"chosen", ChosenId}, {"before", before} };
        }
        public static ActiveEvent FromJson(Dictionary<string, object> d)
        {
            var a = new ActiveEvent { EventId = J.Str(d, "id"), EventVersion = J.Int(d, "version"), SnapshotJson = J.Str(d, "snapshot"),
                PageIndex = J.Int(d, "page"), Phase = J.Str(d, "phase", "pages"), ChosenId = J.Str(d, "chosen") };
            var c = J.Child(d, "cast");
            if (c != null) foreach (var kv in c) a.Cast[kv.Key] = Convert.ToInt32(kv.Value);
            var b = J.Child(d, "before");
            if (b != null) foreach (var kv in b) a.Before[kv.Key] = Convert.ToInt32(kv.Value);
            return a;
        }
    }

    public sealed class Family
    {
        public string Name = "";
        public int Today;
        public int Mood = 128;       // 0..255, 5단계 (확인됨: mood_levels)
        public long Assets;
        public int HouseGrade;
        public int NextPersonId = 1;
        public int StartDay;                                   // 가족 시작일 → 상단 "N년가족"
        public int Gratitude;                                  // 신님에게 감사 (개수)
        public int HeadId = -1;                                // 세대주
        public Dictionary<string, int> Items = new Dictionary<string, int>();   // 화살·아이템 보유 수
        public int YearsAsFamily { get { return GameDate.AgeYears(StartDay, Today); } }
        public List<Person> Members = new List<Person>();
        public HashSet<string> Flags = new HashSet<string>();
        public Dictionary<string, int> LastFired = new Dictionary<string, int>();     // 이벤트id|인물 -> 일자
        public Dictionary<string, int> FireCount = new Dictionary<string, int>();     // 이벤트id|인물 -> 횟수
        public List<EventRecord> History = new List<EventRecord>();
        public ActiveEvent Active;
        public ulong RngState = 12345;
        public List<ActiveEvent> Queue = new List<ActiveEvent>();                     // 대기 중 이벤트
        /// <summary>원작 코드로 진행하는 가족이면 원작 메모리(세이브 영역+난수 seed, base64). 비어 있으면 팩 규칙 가족.</summary>
        public string OrigState = "";
        /// <summary>원작 가족의 표시 이름 (인물 번호 → 이름). 원작 이름 글자표는 아직 해독하지 못해 앱이 붙인 이름이다.</summary>
        public Dictionary<int, string> OrigNames = new Dictionary<int, string>();
        public bool IsOriginal { get { return !string.IsNullOrEmpty(OrigState); } }

        public static int MoodLevel(int mood)
        {
            if (mood < 48) return 1; if (mood < 96) return 2; if (mood < 160) return 3; if (mood < 208) return 4; return 5;
        }
        public Person Get(int id) { foreach (var p in Members) if (p.Id == id) return p; return null; }
    }
}
